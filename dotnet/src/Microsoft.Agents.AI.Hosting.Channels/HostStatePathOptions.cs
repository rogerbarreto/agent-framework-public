// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// File-system layout for <see cref="FileHostStateStore"/>. All paths optional; per-component paths
/// derive from <see cref="Root"/> when unset. v1 owns only reset-session aliases and workflow checkpoint
/// path derivation.
/// </summary>
public sealed record HostStatePathOptions
{
    /// <summary>Root directory under which per-component subpaths are derived.</summary>
    public string? Root { get; init; }

    /// <summary>Path for reset-session aliases.</summary>
    public string? AliasesPath { get; init; }

    /// <summary>Root path for per-isolation-key workflow checkpoint derivation.</summary>
    public string? CheckpointsPath { get; init; }
}
