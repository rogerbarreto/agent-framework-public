// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Pluggable seam for scheduling, executing, and persisting background tasks. Backs the host's
/// non-originating push fan-out via the reserved <c>"hosting.push"</c> handler and surfaces
/// background-run tracking via <see cref="ContinuationToken"/>.
/// </summary>
public interface IDurableTaskRunner
{
    /// <summary>How this runner serializes payloads. Read by the startup codec/runner pairing validator.</summary>
    DurableTaskPayloadMode PayloadMode { get; }

    /// <summary>Register a handler under a name. The host registers <c>"hosting.push"</c> at startup.</summary>
    void Register(string name, Func<TaskInvocationContext, ValueTask> handler);

    /// <summary>Schedule a task under a previously-registered handler.</summary>
    ValueTask<TaskHandle> ScheduleAsync(
        string name,
        object payload,
        RetryPolicy? retryPolicy,
        CancellationToken cancellationToken);

    /// <summary>Read the current status of a scheduled task.</summary>
    ValueTask<DurableTaskStatus?> GetAsync(TaskHandle handle, CancellationToken cancellationToken);

    /// <summary>Cancel a scheduled task.</summary>
    ValueTask CancelAsync(TaskHandle handle, CancellationToken cancellationToken);
}