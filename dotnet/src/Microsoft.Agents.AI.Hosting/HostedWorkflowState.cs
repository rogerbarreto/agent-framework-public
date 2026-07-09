// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting;

/// <summary>
/// Optional shared execution state for applications that own their own hosting route and want to expose a
/// workflow with per-session checkpoint resume. Pairs a <see cref="Workflow"/> target with a
/// <see cref="CheckpointManager"/> and an application-scoped <c>sessionId -&gt; CheckpointInfo</c> head cursor.
/// </summary>
/// <remarks>
/// <para>
/// The .NET workflow checkpoint store is already keyed by session id, but <see cref="CheckpointInfo"/> carries
/// no ordering, so this holder remembers the head checkpoint per session to resume the correct one. It does not
/// own routing, authentication, or storage policy.
/// </para>
/// <para>
/// The default cursor is in-memory and does not survive process restarts; durable or multi-replica hosts should
/// supply a durable <see cref="CheckpointManager"/> and record the returned <see cref="HostedWorkflowRunResult.Checkpoint"/>
/// in their own durable cursor.
/// </para>
/// <para>
/// <strong>Trust boundary.</strong> <c>sessionId</c> is an application-selected partition key. When it originates
/// from the wire, the application must authenticate the caller and authorize the key before using it here. The
/// checkpoint boundary must be at least as specific as the authorized session boundary.
/// </para>
/// </remarks>
public sealed class HostedWorkflowState
{
    private readonly CheckpointManager _checkpointManager;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, CheckpointInfo> _cursor = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedWorkflowState"/> class.
    /// </summary>
    /// <param name="workflow">The workflow target.</param>
    /// <param name="checkpointManager">
    /// The checkpoint manager to use. Defaults to <see cref="CheckpointManager.CreateInMemory"/> when not provided.
    /// </param>
    /// <param name="loggerFactory">
    /// The logger factory used to report resume diagnostics (for example, a resume turn that made no progress).
    /// Defaults to <see cref="NullLoggerFactory"/> when not provided.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="workflow"/> is <see langword="null"/>.</exception>
    public HostedWorkflowState(Workflow workflow, CheckpointManager? checkpointManager = null, ILoggerFactory? loggerFactory = null)
    {
        _ = Throw.IfNull(workflow);

        this.Workflow = workflow;
        this._checkpointManager = checkpointManager ?? CheckpointManager.CreateInMemory();
        this._logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger(typeof(HostedWorkflowState));
    }

    /// <summary>
    /// Gets the workflow target.
    /// </summary>
    public Workflow Workflow { get; }

    /// <summary>
    /// Runs the workflow forward for <paramref name="sessionId"/> with checkpointing on the first turn, or, on
    /// subsequent turns, restores the session's recorded head checkpoint and then runs the workflow forward with
    /// the new turn's <paramref name="input"/>. The new head checkpoint is recorded for the session afterwards.
    /// </summary>
    /// <remarks>
    /// The resume semantics mirror the Python hosting host (<c>agent_framework_hosting</c>'s <c>_invoke_workflow</c>):
    /// each turn restores the latest checkpoint to rehydrate accumulated workflow state and then applies the new
    /// input, rather than continuing a halted run with no input (which would leave the run waiting for input
    /// indefinitely). For agent (chat-protocol) workflows the new input is accompanied by a
    /// <see cref="TurnToken"/> so the turn is driven, matching the fresh-run path.
    /// </remarks>
    /// <typeparam name="TInput">The workflow input type.</typeparam>
    /// <param name="sessionId">The application-selected session id.</param>
    /// <param name="input">The input to run on this turn (used both when starting a new run and when resuming).</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The run result, including the events emitted on this turn and the recorded head checkpoint.</returns>
    public async ValueTask<HostedWorkflowRunResult> RunOrResumeAsync<TInput>(string sessionId, TInput input, CancellationToken cancellationToken = default)
        where TInput : notnull
    {
        _ = Throw.IfNullOrEmpty(sessionId);
        _ = Throw.IfNull(input);

        if (!this._cursor.TryGetValue(sessionId, out CheckpointInfo? head))
        {
            // First turn for this session: run the workflow forward from its start executor with the input.
            Run freshRun = await InProcessExecution.RunAsync(this.Workflow, input, this._checkpointManager, sessionId, cancellationToken).ConfigureAwait(false);
            await using (freshRun.ConfigureAwait(false))
            {
                return this.Record(sessionId, freshRun.OutgoingEvents.ToList(), freshRun.LastCheckpoint);
            }
        }

        // Subsequent turn: restore the session's latest checkpoint to rehydrate accumulated workflow state, then
        // run the workflow forward with the new turn's input. Agent workflows use the chat protocol, which requires
        // a TurnToken to drive the turn (mirroring how the fresh-run path seeds one).
        //
        // The streaming resume restores state without draining to a halt first; the non-streaming resume would
        // block waiting for input immediately after restore (before we can deliver the new input).
        ProtocolDescriptor descriptor = await this.Workflow.DescribeProtocolAsync(cancellationToken).ConfigureAwait(false);

        StreamingRun resumed = await InProcessExecution.ResumeStreamingAsync(this.Workflow, head, this._checkpointManager, cancellationToken).ConfigureAwait(false);
        await using (resumed.ConfigureAwait(false))
        {
            await resumed.TrySendMessageAsync(input).ConfigureAwait(false);
            if (descriptor.IsChatProtocol() && input is not TurnToken)
            {
                await resumed.TrySendMessageAsync(new TurnToken(emitEvents: true)).ConfigureAwait(false);
            }

            List<WorkflowEvent> events = [];
            await foreach (WorkflowEvent evt in resumed.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                events.Add(evt);

                // Stop draining when the resumed workflow halts awaiting an external response. The public
                // stream blocks indefinitely on an unserviced pending request, whereas the first-turn
                // RunAsync path returns at this same halt (Run.RunToNextHaltAsync uses non-blocking drain).
                // SuperStepCompletedEvent marks the end of the pausing superstep and carries the flag.
                if (evt is SuperStepCompletedEvent { CompletionInfo.HasPendingRequests: true })
                {
                    break;
                }
            }

            if (events.Count == 0)
            {
                // The resumed turn drove no work. This mirrors the Python host's zero-event restore warning:
                // the checkpoint may be stale or the input may not match the workflow's expected type, so the
                // session's state may not have progressed.
                this._logger.LogWarning(
                    "Resuming workflow session '{SessionId}' produced no events; the checkpoint may be stale or the input may not match the workflow's expected input type. Session state may not have progressed.",
                    sessionId);
            }

            return this.Record(sessionId, events, resumed.LastCheckpoint);
        }
    }

    private HostedWorkflowRunResult Record(string sessionId, List<WorkflowEvent> events, CheckpointInfo? checkpoint)
    {
        if (checkpoint is not null)
        {
            this._cursor[sessionId] = checkpoint;
        }

        return new HostedWorkflowRunResult(sessionId, events, checkpoint);
    }

    /// <summary>
    /// Gets the recorded head checkpoint for <paramref name="sessionId"/>, if any.
    /// </summary>
    /// <param name="sessionId">The application-selected session id.</param>
    /// <param name="checkpoint">When this method returns, the recorded head checkpoint, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a checkpoint is recorded for the session; otherwise <see langword="false"/>.</returns>
    public bool TryGetCheckpoint(string sessionId, out CheckpointInfo? checkpoint)
    {
        _ = Throw.IfNullOrEmpty(sessionId);
        return this._cursor.TryGetValue(sessionId, out checkpoint);
    }
}
