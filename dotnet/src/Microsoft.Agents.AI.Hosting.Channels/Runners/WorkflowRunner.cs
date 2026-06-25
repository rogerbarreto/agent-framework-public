// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Default <see cref="IHostedTargetRunner"/> for <see cref="Workflow"/> targets. Drives execution via
/// <see cref="InProcessExecution"/> and projects pause / completion / failure into
/// <see cref="WorkflowRunResult"/>.
/// </summary>
/// <remarks>
/// v1 runs forward and surfaces <see cref="WorkflowRunStatus.AwaitingInput"/> on a
/// <see cref="RequestInfoEvent"/>. Resume is caller-driven via a channel-supplied checkpoint reference on
/// <see cref="ChannelRequest.Attributes"/> (<c>"workflow.checkpoint_id"</c>); the host owns no continuation
/// store in v1.
/// </remarks>
public sealed class WorkflowRunner : IHostedTargetRunner
{
    /// <summary>Attribute key for caller-supplied checkpoint resume.</summary>
    public const string CheckpointIdAttribute = "workflow.checkpoint_id";

    /// <summary>Initializes a new instance.</summary>
    public WorkflowRunner(Workflow workflow)
    {
        this.Workflow = Throw.IfNull(workflow);
    }

    /// <summary>The wrapped workflow.</summary>
    public Workflow Workflow { get; }

    /// <inheritdoc />
    public async ValueTask<HostedRunResult> RunAsync(ChannelRequest request, CancellationToken cancellationToken)
    {
        Throw.IfNull(request);
        var run = await InProcessExecution.RunStreamingAsync(this.Workflow, request.Input, request.Session?.Key, cancellationToken).ConfigureAwait(false);

        var outputs = new List<object?>();
        ExternalRequest? pending = null;

        await foreach (var evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case RequestInfoEvent rie:
                    pending = rie.Request;
                    break;
                case WorkflowOutputEvent woe:
                    outputs.Add(woe.Data);
                    break;
                case WorkflowErrorEvent err:
                    return Build(new WorkflowRunResult { Status = WorkflowRunStatus.Failed, Error = err.Data?.ToString(), Outputs = outputs, SessionId = run.SessionId }, request.Session);
            }
        }

        var session = new ChannelSession(request.Session ?? new ChannelSession()) { Key = run.SessionId };
        var result = pending is not null
            ? new WorkflowRunResult { Status = WorkflowRunStatus.AwaitingInput, PendingRequest = pending, Outputs = outputs, SessionId = run.SessionId }
            : new WorkflowRunResult { Status = WorkflowRunStatus.Completed, Outputs = outputs, SessionId = run.SessionId };
        return Build(result, session);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<HostedStreamItem> StreamAsync(
        ChannelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Throw.IfNull(request);
        var run = await InProcessExecution.RunStreamingAsync(this.Workflow, request.Input, request.Session?.Key, cancellationToken).ConfigureAwait(false);

        var outputs = new List<object?>();
        ExternalRequest? pending = null;

        await foreach (var evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new HostedStreamEvent(evt);
            switch (evt)
            {
                case RequestInfoEvent rie:
                    pending = rie.Request;
                    break;
                case WorkflowOutputEvent woe:
                    outputs.Add(woe.Data);
                    break;
            }
        }

        var session = new ChannelSession(request.Session ?? new ChannelSession()) { Key = run.SessionId };
        var result = pending is not null
            ? new WorkflowRunResult { Status = WorkflowRunStatus.AwaitingInput, PendingRequest = pending, Outputs = outputs, SessionId = run.SessionId }
            : new WorkflowRunResult { Status = WorkflowRunStatus.Completed, Outputs = outputs, SessionId = run.SessionId };
        yield return new HostedStreamCompleted(Build(result, session));
    }

    private static HostedRunResult<WorkflowRunResult> Build(WorkflowRunResult result, ChannelSession? session) =>
        new(result) { Session = session };
}
