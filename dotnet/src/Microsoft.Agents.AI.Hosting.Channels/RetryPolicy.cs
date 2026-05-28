// Copyright (c) Microsoft. All rights reserved.

using System;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Retry parameters for a scheduled durable task. Mirrors the Python runner defaults so behaviour
/// is consistent across language SDKs.
/// </summary>
public sealed record RetryPolicy
{
    /// <summary>Total attempt count (initial attempt + retries). Default 5.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Delay before the first retry. Default 1 second.</summary>
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Multiplier applied to the previous backoff. Default 2.0.</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>Cap on a single backoff delay. Default 60 seconds.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The default retry policy used when callers omit one.</summary>
    public static RetryPolicy Default { get; } = new();
}