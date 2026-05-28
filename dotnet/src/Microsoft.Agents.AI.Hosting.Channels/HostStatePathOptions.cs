// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// File-system layout for the file-backed host state store. All paths are optional; when a
/// per-component path is omitted the store derives it from <see cref="Root"/>.
/// </summary>
public sealed record HostStatePathOptions
{
    /// <summary>Root directory under which per-component subpaths are derived.</summary>
    public string? Root { get; init; }

    /// <summary>Path used by <see cref="InProcessDurableTaskRunner"/> for persistent task records.</summary>
    public string? RunnerPath { get; init; }

    /// <summary>Path used for the identity registry and pending link grants.</summary>
    public string? LinksPath { get; init; }

    /// <summary>Path used for continuation tokens.</summary>
    public string? ContinuationsPath { get; init; }

    /// <summary>Path used for last-seen ledger entries that back <see cref="ResponseTarget.Active"/>.</summary>
    public string? LastSeenPath { get; init; }
}