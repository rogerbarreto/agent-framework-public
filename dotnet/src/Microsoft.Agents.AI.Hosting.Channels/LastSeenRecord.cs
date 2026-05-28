// Copyright (c) Microsoft. All rights reserved.

using System;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Most recent channel activity observed for an isolation key. Backs <see cref="ResponseTarget.Active"/>.
/// </summary>
/// <param name="Identity">The full channel-native identity last seen (not just the channel name).</param>
/// <param name="ConversationId">The conversation last seen, when applicable.</param>
/// <param name="At">Timestamp of the observation.</param>
public sealed record LastSeenRecord(
    ChannelIdentity Identity,
    string? ConversationId,
    DateTimeOffset At);