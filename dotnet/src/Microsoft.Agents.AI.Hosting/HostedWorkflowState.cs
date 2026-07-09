// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
/// The in-memory head cursor accelerates the common case, but when it misses (for example a new holder or a
/// process restart) the holder falls back to <see cref="CheckpointManager.GetLatestCheckpointAsync"/>. A durable
/// <see cref="CheckpointManager"/> therefore resumes correctly across restarts; the default in-memory manager does
/// not persist, so with it a restart starts the session fresh.
/// </para>
/// <para>
/// <strong>Trust boundary.</strong> <c>sessionId</c> is an application-selected partition key. When it originates
/// from the wire, the application must authenticate the caller and authorize the key before using it here. The
/// checkpoint boundary must be at least as specific as the authorized session boundary.
/// </para>
/// </remarks>
public sealed class HostedWorkflowState : IDisposable
{
    private readonly CheckpointManager _checkpointManager;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, CheckpointInfo> _cursor = new(StringComparer.Ordinal);

    // A single workflow instance backs every session on this holder, and workflow instances do not support
    // concurrent runs, so all turns are serialized through one lock (mirroring the Python host's workflow lock).
    private readonly SemaphoreSlim _workflowLock = new(1, 1);

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

        // Serialize turns: the shared workflow instance cannot be run by two runners at once, and concurrent
        // same-session turns would otherwise race the head cursor.
        await this._workflowLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await this.RunOrResumeCoreAsync(sessionId, input, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this._workflowLock.Release();
        }
    }

    private async ValueTask<HostedWorkflowRunResult> RunOrResumeCoreAsync<TInput>(string sessionId, TInput input, CancellationToken cancellationToken)
        where TInput : notnull
    {
        if (!this._cursor.TryGetValue(sessionId, out CheckpointInfo? head))
        {
            // The in-memory cursor is empty for this session. Fall back to the checkpoint manager so a durable
            // manager still resumes after the cursor is lost (for example a process restart or a new holder over
            // the same store), mirroring the Python host's per-turn get_latest read-through.
            head = await this._checkpointManager.GetLatestCheckpointAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }

        if (head is null)
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
            // Drain non-blocking on pending requests, matching the first-turn RunAsync path
            // (Run.RunToNextHaltAsync also uses blockOnPendingRequest: false): the workflow may halt awaiting an
            // external response, and blocking there would wait indefinitely.
            await foreach (WorkflowEvent evt in resumed.WatchStreamAsync(blockOnPendingRequest: false, cancellationToken).ConfigureAwait(false))
            {
                events.Add(evt);
            }

            if (events.Count == 0)
            {
                this.WarnOnNoProgress(sessionId);
            }

            return this.Record(sessionId, events, resumed.LastCheckpoint);
        }
    }

    /// <summary>
    /// Streams the events of a run-or-resume turn as they occur, applying the same restore-then-run semantics as
    /// <see cref="RunOrResumeAsync{TInput}(string, TInput, CancellationToken)"/>: the first turn runs the workflow
    /// forward from its start executor, and subsequent turns restore the session's latest checkpoint and run
    /// forward with <paramref name="input"/>. The session's head checkpoint is recorded when the stream ends,
    /// including when the consumer abandons enumeration early.
    /// </summary>
    /// <remarks>
    /// Turns are serialized through the holder's workflow lock, which is held for the lifetime of the returned
    /// enumerator. The caller must enumerate the stream to completion (or dispose it) to release the lock. The head
    /// checkpoint is recorded from the run's last committed checkpoint when the stream ends — whether it completes
    /// normally or the consumer disposes it early — so an interrupted turn still advances the session cursor.
    /// </remarks>
    /// <typeparam name="TInput">The workflow input type.</typeparam>
    /// <param name="sessionId">The application-selected session id.</param>
    /// <param name="input">The input to run on this turn.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>An asynchronous stream of the <see cref="WorkflowEvent"/>s emitted during this turn.</returns>
    public async IAsyncEnumerable<WorkflowEvent> RunOrResumeStreamingAsync<TInput>(string sessionId, TInput input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TInput : notnull
    {
        _ = Throw.IfNullOrEmpty(sessionId);
        _ = Throw.IfNull(input);

        await this._workflowLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!this._cursor.TryGetValue(sessionId, out CheckpointInfo? head))
            {
                head = await this._checkpointManager.GetLatestCheckpointAsync(sessionId, cancellationToken).ConfigureAwait(false);
            }

            ProtocolDescriptor descriptor = await this.Workflow.DescribeProtocolAsync(cancellationToken).ConfigureAwait(false);

            // The fresh streaming run enqueues the input itself; the streaming resume restores state and needs the
            // input delivered explicitly. Neither streaming entry point seeds a TurnToken, so drive chat-protocol
            // workflows with one on both paths.
            StreamingRun run = head is null
                ? await InProcessExecution.RunStreamingAsync(this.Workflow, input, this._checkpointManager, sessionId, cancellationToken).ConfigureAwait(false)
                : await InProcessExecution.ResumeStreamingAsync(this.Workflow, head, this._checkpointManager, cancellationToken).ConfigureAwait(false);

            await using (run.ConfigureAwait(false))
            {
                if (head is not null)
                {
                    await run.TrySendMessageAsync(input).ConfigureAwait(false);
                }

                if (descriptor.IsChatProtocol() && input is not TurnToken)
                {
                    await run.TrySendMessageAsync(new TurnToken(emitEvents: true)).ConfigureAwait(false);
                }

                int eventCount = 0;
                try
                {
                    // Drain non-blocking on pending requests (see RunOrResumeCoreAsync) so a workflow that halts
                    // awaiting an external response ends the stream instead of blocking indefinitely.
                    await foreach (WorkflowEvent evt in run.WatchStreamAsync(blockOnPendingRequest: false, cancellationToken).ConfigureAwait(false))
                    {
                        eventCount++;
                        yield return evt;
                    }

                    if (eventCount == 0 && head is not null)
                    {
                        this.WarnOnNoProgress(sessionId);
                    }
                }
                finally
                {
                    // Record the head checkpoint even when the consumer abandons the stream (for example an SSE
                    // client disconnect), so an interrupted turn still advances the session cursor to the last
                    // committed checkpoint and a later turn resumes from there rather than re-running prior work.
                    this.UpdateCursor(sessionId, run.LastCheckpoint);
                }
            }
        }
        finally
        {
            this._workflowLock.Release();
        }
    }

    private HostedWorkflowRunResult Record(string sessionId, List<WorkflowEvent> events, CheckpointInfo? checkpoint)
    {
        this.UpdateCursor(sessionId, checkpoint);
        return new HostedWorkflowRunResult(sessionId, events, checkpoint);
    }

    private void UpdateCursor(string sessionId, CheckpointInfo? checkpoint)
    {
        if (checkpoint is not null)
        {
            this._cursor[sessionId] = checkpoint;
        }
    }

    private void WarnOnNoProgress(string sessionId)
        // The resumed turn drove no work. This mirrors the Python host's zero-event restore warning: the
        // checkpoint may be stale or the input may not match the workflow's expected type, so the session's
        // state may not have progressed.
        => this._logger.LogWarning(
            "Resuming workflow session '{SessionId}' produced no events; the checkpoint may be stale or the input may not match the workflow's expected input type. Session state may not have progressed.",
            sessionId);

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

    /// <summary>
    /// Releases the resources used by this instance, including the workflow serialization lock.
    /// </summary>
    public void Dispose() => this._workflowLock.Dispose();
}
