// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Result of a successful <see cref="IIdentityLinker.CompleteAsync"/> call.
/// </summary>
/// <param name="IsolationKey">The resolved isolation key.</param>
/// <param name="Identity">The channel-native identity that completed the link.</param>
/// <param name="VerifiedClaims">Claims verified by the linker (e.g. AAD <c>oid</c>, email).</param>
public sealed record PrincipalIdentity(
    string IsolationKey,
    ChannelIdentity Identity,
    IReadOnlyDictionary<string, string> VerifiedClaims);