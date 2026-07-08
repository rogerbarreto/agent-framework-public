// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;

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
    private readonly ConcurrentDictionary<string, CheckpointInfo> _cursor = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedWorkflowState"/> class.
    /// </summary>
    /// <param name="workflow">The workflow target.</param>
    /// <param name="checkpointManager">
    /// The checkpoint manager to use. Defaults to <see cref="CheckpointManager.CreateInMemory"/> when not provided.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="workflow"/> is <see langword="null"/>.</exception>
    public HostedWorkflowState(Workflow workflow, CheckpointManager? checkpointManager = null)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        this.Workflow = workflow;
        this._checkpointManager = checkpointManager ?? CheckpointManager.CreateInMemory();
    }

    /// <summary>
    /// Gets the workflow target.
    /// </summary>
    public Workflow Workflow { get; }

    /// <summary>
    /// Runs the workflow for <paramref name="sessionId"/> with checkpointing, or resumes from the session's
    /// recorded head checkpoint when one exists, then records the new head checkpoint for the session.
    /// </summary>
    /// <typeparam name="TInput">The workflow input type.</typeparam>
    /// <param name="sessionId">The application-selected session id.</param>
    /// <param name="input">The input used when starting a new run (ignored when resuming).</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The run result, including the emitted events and the recorded head checkpoint.</returns>
    public async ValueTask<HostedWorkflowRunResult> RunOrResumeAsync<TInput>(string sessionId, TInput input, CancellationToken cancellationToken = default)
        where TInput : notnull
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentNullException.ThrowIfNull(input);

        Run run = this._cursor.TryGetValue(sessionId, out CheckpointInfo? head)
            ? await InProcessExecution.ResumeAsync(this.Workflow, head, this._checkpointManager, cancellationToken).ConfigureAwait(false)
            : await InProcessExecution.RunAsync(this.Workflow, input, this._checkpointManager, sessionId, cancellationToken).ConfigureAwait(false);

        await using (run.ConfigureAwait(false))
        {
            var events = run.OutgoingEvents.ToList();
            CheckpointInfo? checkpoint = run.LastCheckpoint;
            if (checkpoint is not null)
            {
                this._cursor[sessionId] = checkpoint;
            }

            return new HostedWorkflowRunResult(sessionId, events, checkpoint);
        }
    }

    /// <summary>
    /// Gets the recorded head checkpoint for <paramref name="sessionId"/>, if any.
    /// </summary>
    /// <param name="sessionId">The application-selected session id.</param>
    /// <param name="checkpoint">When this method returns, the recorded head checkpoint, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a checkpoint is recorded for the session; otherwise <see langword="false"/>.</returns>
    public bool TryGetCheckpoint(string sessionId, out CheckpointInfo? checkpoint)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        return this._cursor.TryGetValue(sessionId, out checkpoint);
    }
}
