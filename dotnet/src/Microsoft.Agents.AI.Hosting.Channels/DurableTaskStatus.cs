// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Lifecycle state of a scheduled durable task.
/// </summary>
public enum DurableTaskStatus
{
    /// <summary>Queued, not yet picked up by a worker.</summary>
    Scheduled,

    /// <summary>Currently executing.</summary>
    Running,

    /// <summary>Completed successfully.</summary>
    Succeeded,

    /// <summary>Failed after exhausting the retry policy.</summary>
    Failed,

    /// <summary>Cancelled before reaching a terminal state.</summary>
    Cancelled,
}