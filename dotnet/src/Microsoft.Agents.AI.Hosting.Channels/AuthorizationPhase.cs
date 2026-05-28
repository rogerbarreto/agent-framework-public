// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Phase of the authorization pipeline at which an allowlist is being evaluated.
/// </summary>
public enum AuthorizationPhase
{
    /// <summary>The channel has not (yet) presented linked-claim evidence for the identity.</summary>
    PreLink,

    /// <summary>The identity has been linked via <see cref="IIdentityLinker"/> and verified claims are available.</summary>
    PostLink,
}