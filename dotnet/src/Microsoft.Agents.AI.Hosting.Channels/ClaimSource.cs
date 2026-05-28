// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Where the verified claims on an <see cref="AuthorizationContext"/> originated.
/// </summary>
public enum ClaimSource
{
    /// <summary>No verified claims are present.</summary>
    None,

    /// <summary>Claims came from the channel itself (e.g. an Activity Protocol bearer token's AAD object id).</summary>
    Channel,

    /// <summary>Claims came from a completed <see cref="IIdentityLinker"/> ceremony.</summary>
    Linker,
}