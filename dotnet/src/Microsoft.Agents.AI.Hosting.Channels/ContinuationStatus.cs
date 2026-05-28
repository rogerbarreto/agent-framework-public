// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Lifecycle state of a <see cref="ContinuationToken"/>.
/// </summary>
public enum ContinuationStatus
{
    /// <summary>The run is queued and not yet started.</summary>
    Queued,

    /// <summary>The run is executing.</summary>
    Running,

    /// <summary>The run completed; <see cref="ContinuationToken.Result"/> carries the value.</summary>
    Completed,

    /// <summary>The run failed; <see cref="ContinuationToken.Error"/> carries the reason.</summary>
    Failed,
}