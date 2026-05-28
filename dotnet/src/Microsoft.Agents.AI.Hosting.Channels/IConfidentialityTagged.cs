// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Capability interface: a channel can tag itself with a confidentiality tier. Read by
/// <see cref="SameConfidentialityTierLinkPolicy"/> to decide whether two channels may share state
/// or deliver to one another.
/// </summary>
public interface IConfidentialityTagged
{
    /// <summary>Opaque confidentiality tier label; <see langword="null"/> means single-tier.</summary>
    string? ConfidentialityTier { get; }
}