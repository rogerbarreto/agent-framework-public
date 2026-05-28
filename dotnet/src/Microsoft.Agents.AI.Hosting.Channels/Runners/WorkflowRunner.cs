// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Default <see cref="IHostedTargetRunner"/> for <see cref="Workflow"/> targets. Drives execution
/// via <see cref="InProcessExecution"/> and projects pause / completion / failure into
/// <see cref="WorkflowRunResult"/>.
/// </summary>
/// <remarks>
/// Resume tokens map to in-memory <see cref="StreamingRun"/> instances for this draft. They
/// survive only the lifetime of the process; durable replay across restarts requires an external
/// <see cref="IDurableTaskRunner"/> + checkpoint storage and lands in a follow-up commit.
/// </remarks>
public sealed class WorkflowRunner : IHostedTargetRunner
{
    /// <summary>Attribute key carried on <see cref="ChannelRequest.Attributes"/> to resume a paused workflow.</summary>
    public const string ResumeTokenAttribute = "workflow.resume_token";

    /// <summary>Attribute key carried on <see cref="ChannelRequest.Attributes"/> for direct checkpoint resume.</summary>
    public const string CheckpointIdAttribute = "workflow.checkpoint_id";

    private readonly Workflow _workflow;
    private readonly IHostStateStore _stateStore;
    private readonly ConcurrentDictionary<string, ResumeEntry> _resumeEntries = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance.</summary>
    public WorkflowRunner(Workflow workflow, IHostStateStore stateStore)
    {
        this._workflow = Throw.IfNull(workflow);
        this._stateStore = Throw.IfNull(stateStore);
    }

    /// <summary>The wrapped workflow.</summary>
    public Workflow Workflow => this._workflow;

    /// <inheritdoc />
    public async ValueTask<HostedRunResult> RunAsync(ChannelRequest request, CancellationToken cancellationToken)
    {
        Throw.IfNull(request);

        if (request.Attributes.TryGetValue(ResumeTokenAttribute, out var rawToken) && rawToken is string resumeToken)
        {
            return await this.ResumeAsync(resumeToken, request, cancellationToken).ConfigureAwait(false);
        }

        var run = await InProcessExecution.RunStreamingAsync(this._workflow, request.Input, request.Session?.Key, cancellationToken).ConfigureAwait(false);
        return await this.DriveAsync(run, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<HostedStreamItem> StreamAsync(
        ChannelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Throw.IfNull(request);

        if (request.Attributes.TryGetValue(ResumeTokenAttribute, out var rawToken) && rawToken is string resumeToken)
        {
            await foreach (var item in this.StreamResumeAsync(resumeToken, request, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
            yield break;
        }

        var run = await InProcessExecution.RunStreamingAsync(this._workflow, request.Input, request.Session?.Key, cancellationToken).ConfigureAwait(false);
        await foreach (var item in this.WatchAsync(run, request, cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    private async ValueTask<HostedRunResult> ResumeAsync(string resumeToken, ChannelRequest request, CancellationToken cancellationToken)
    {
        if (!this._resumeEntries.TryRemove(resumeToken, out var entry))
        {
            return BuildResult(
                new WorkflowRunResult { Status = WorkflowRunStatus.Failed, Error = $"Resume token '{resumeToken}' is unknown or already consumed.", SessionId = request.Session?.Key },
                request.Session);
        }

        await entry.Run.SendResponseAsync(entry.PendingRequest.CreateResponse(request.Input)).ConfigureAwait(false);
        await this._stateStore.DeleteContinuationAsync(resumeToken, cancellationToken).ConfigureAwait(false);
        return await this.DriveAsync(entry.Run, request, cancellationToken).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<HostedStreamItem> StreamResumeAsync(
        string resumeToken,
        ChannelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!this._resumeEntries.TryRemove(resumeToken, out var entry))
        {
            yield return new HostedStreamCompleted(BuildResult(
                new WorkflowRunResult { Status = WorkflowRunStatus.Failed, Error = $"Resume token '{resumeToken}' is unknown or already consumed.", SessionId = request.Session?.Key },
                request.Session));
            yield break;
        }

        await entry.Run.SendResponseAsync(entry.PendingRequest.CreateResponse(request.Input)).ConfigureAwait(false);
        await this._stateStore.DeleteContinuationAsync(resumeToken, cancellationToken).ConfigureAwait(false);

        await foreach (var item in this.WatchAsync(entry.Run, request, cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    private async ValueTask<HostedRunResult> DriveAsync(StreamingRun run, ChannelRequest request, CancellationToken cancellationToken)
    {
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
                    return BuildResult(
                        new WorkflowRunResult { Status = WorkflowRunStatus.Failed, Error = err.Data?.ToString(), Outputs = outputs, SessionId = run.SessionId },
                        request.Session);
            }
        }

        if (pending is not null)
        {
            var resumeToken = Guid.NewGuid().ToString("N");
            this._resumeEntries[resumeToken] = new ResumeEntry(run, pending);
            await this._stateStore.SaveContinuationAsync(
                new ContinuationToken
                {
                    Token = resumeToken,
                    Status = ContinuationStatus.Queued,
                    IsolationKey = request.Session?.IsolationKey,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken).ConfigureAwait(false);

            var session = (request.Session ?? new ChannelSession()) with
            {
                Key = run.SessionId,
                Attributes = MergeAttribute(request.Session?.Attributes, ResumeTokenAttribute, resumeToken),
            };

            return BuildResult(
                new WorkflowRunResult { Status = WorkflowRunStatus.AwaitingInput, PendingRequest = pending, Outputs = outputs, SessionId = run.SessionId },
                session);
        }

        return BuildResult(
            new WorkflowRunResult { Status = WorkflowRunStatus.Completed, Outputs = outputs, SessionId = run.SessionId },
            (request.Session ?? new ChannelSession()) with { Key = run.SessionId });
    }

    private async IAsyncEnumerable<HostedStreamItem> WatchAsync(
        StreamingRun run,
        ChannelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
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

        ChannelSession? session = request.Session;
        WorkflowRunResult final;
        if (pending is not null)
        {
            var resumeToken = Guid.NewGuid().ToString("N");
            this._resumeEntries[resumeToken] = new ResumeEntry(run, pending);
            await this._stateStore.SaveContinuationAsync(
                new ContinuationToken
                {
                    Token = resumeToken,
                    Status = ContinuationStatus.Queued,
                    IsolationKey = request.Session?.IsolationKey,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken).ConfigureAwait(false);
            session = (session ?? new ChannelSession()) with
            {
                Key = run.SessionId,
                Attributes = MergeAttribute(session?.Attributes, ResumeTokenAttribute, resumeToken),
            };
            final = new WorkflowRunResult { Status = WorkflowRunStatus.AwaitingInput, PendingRequest = pending, Outputs = outputs, SessionId = run.SessionId };
        }
        else
        {
            session = (session ?? new ChannelSession()) with { Key = run.SessionId };
            final = new WorkflowRunResult { Status = WorkflowRunStatus.Completed, Outputs = outputs, SessionId = run.SessionId };
        }

        yield return new HostedStreamCompleted(BuildResult(final, session));
    }

    private static HostedRunResult<WorkflowRunResult> BuildResult(WorkflowRunResult result, ChannelSession? session) =>
        new() { Result = result, Session = session };

    private static ImmutableDictionary<string, object?> MergeAttribute(
        IReadOnlyDictionary<string, object?>? existing,
        string key,
        object? value)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);
        if (existing is not null)
        {
            foreach (var (k, v) in existing) { builder[k] = v; }
        }
        builder[key] = value;
        return builder.ToImmutable();
    }

    private sealed record ResumeEntry(StreamingRun Run, ExternalRequest PendingRequest);
}