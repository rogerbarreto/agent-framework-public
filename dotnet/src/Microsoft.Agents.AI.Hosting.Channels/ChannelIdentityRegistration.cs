// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Single row in the host's identity registry: a (channel, native_id) mapping to an isolation key.
/// </summary>
/// <param name="Identity">The channel-native identity.</param>
/// <param name="RegisteredAt">When the mapping was first written.</param>
/// <param name="VerifiedClaims">Verified claims persisted at link time for auto-link replay.</param>
public sealed record ChannelIdentityRegistration(
    ChannelIdentity Identity,
    DateTimeOffset RegisteredAt,
    IReadOnlyDictionary<string, string> VerifiedClaims);