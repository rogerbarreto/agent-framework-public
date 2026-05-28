// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Per-delivery context passed to <see cref="IChannelPush.PushAsync"/>.
/// </summary>
public sealed record ChannelPushContext
{
    /// <summary>The channel-native identity to deliver to.</summary>
    public required ChannelIdentity Destination { get; init; }

    /// <summary>The originating request that produced the payload.</summary>
    public required ChannelRequest OriginatingRequest { get; init; }

    /// <summary>The channel name the request originated on.</summary>
    public required string OriginatingChannel { get; init; }

    /// <summary>Whether this push is the echo of the user input, not the agent reply.</summary>
    public bool IsEcho { get; init; }

    /// <summary>The response target the user originally requested, when non-default.</summary>
    public ResponseTarget? OriginalTarget { get; init; }
}