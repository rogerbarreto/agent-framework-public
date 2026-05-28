// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Composition-time options for <c>AddAgentFrameworkHost(...)</c>.
/// </summary>
public sealed record AgentFrameworkHostOptions
{
    /// <summary>Host-level default allowlist. Per-channel allowlists may override or combine.</summary>
    public IIdentityAllowlist? DefaultAllowlist { get; init; }

    /// <summary>Link policy applied across channels.</summary>
    public ILinkPolicy? LinkPolicy { get; init; }

    /// <summary>File-system layout for the file-backed host state store.</summary>
    public HostStatePathOptions? StatePaths { get; init; }

    /// <summary>Default durable runner name; reserved for fast-follow runner-selection wiring.</summary>
    public string? DefaultDurableRunnerName { get; init; }

    /// <summary>Whether <see cref="InProcessDurableTaskRunner"/> is permitted in ephemeral runtime modes.</summary>
    public bool AllowInProcessRunnerInEphemeralMode { get; init; }
}