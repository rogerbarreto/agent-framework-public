// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using Microsoft.Agents.AI.Workflows;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Result envelope returned by <see cref="WorkflowRunner"/>. Carries the workflow output
/// list plus optional pause-state for HITL flows that emit a <see cref="RequestInfoEvent"/>.
/// </summary>
/// <remarks>
/// When <see cref="Status"/> is <see cref="WorkflowRunStatus.AwaitingInput"/> the channel renders
/// a status-awaiting_input envelope and the resume token lives on
/// <see cref="HostedRunResult.Session"/> attributes under the key <c>"workflow.resume_token"</c>.
/// </remarks>
public sealed record WorkflowRunResult
{
    /// <summary>Lifecycle status the runner reached when control returned.</summary>
    public required WorkflowRunStatus Status { get; init; }

    /// <summary>
    /// Outputs emitted by the workflow (from <c>WorkflowOutputEvent</c>). Order matches event order.
    /// </summary>
    public IReadOnlyList<object?> Outputs { get; init; } = [];

    /// <summary>
    /// Pending external request that paused execution. Populated when
    /// <see cref="Status"/> is <see cref="WorkflowRunStatus.AwaitingInput"/>.
    /// </summary>
    public ExternalRequest? PendingRequest { get; init; }

    /// <summary>The workflow session id this run is associated with.</summary>
    public string? SessionId { get; init; }

    /// <summary>Failure detail when <see cref="Status"/> is <see cref="WorkflowRunStatus.Failed"/>.</summary>
    public string? Error { get; init; }
}

/// <summary>Lifecycle status of a <see cref="WorkflowRunResult"/>.</summary>
public enum WorkflowRunStatus
{
    /// <summary>The workflow ran to completion.</summary>
    Completed,

    /// <summary>The workflow paused on a <see cref="RequestInfoEvent"/>; resume by passing the resume token.</summary>
    AwaitingInput,

    /// <summary>The workflow run failed.</summary>
    Failed,
}
