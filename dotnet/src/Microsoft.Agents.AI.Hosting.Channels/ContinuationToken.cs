// Copyright (c) Microsoft. All rights reserved.

using System;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Persisted continuation handle for a background or paused run.
/// </summary>
public sealed record ContinuationToken
{
    /// <summary>Opaque token surface the caller correlates against.</summary>
    public required string Token { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public required ContinuationStatus Status { get; init; }

    /// <summary>The isolation key the underlying run is scoped to.</summary>
    public string? IsolationKey { get; init; }

    /// <summary>When the continuation was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the underlying run reached a terminal state, if any.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>The completed result, populated when <see cref="Status"/> is <see cref="ContinuationStatus.Completed"/>.</summary>
    public HostedRunResult? Result { get; init; }

    /// <summary>Failure summary, populated when <see cref="Status"/> is <see cref="ContinuationStatus.Failed"/>.</summary>
    public string? Error { get; init; }

    /// <summary>The response target the run was scheduled with, when non-default.</summary>
    public ResponseTarget? ResponseTarget { get; init; }
}