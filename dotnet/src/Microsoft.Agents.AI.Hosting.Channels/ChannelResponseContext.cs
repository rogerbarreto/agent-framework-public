// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Per-destination context passed to <see cref="IChannelResponseHook.OnResponseAsync"/>.
/// </summary>
public sealed record ChannelResponseContext
{
    /// <summary>The originating request.</summary>
    public required ChannelRequest Request { get; init; }

    /// <summary>The destination channel for this delivery.</summary>
    public required string ChannelName { get; init; }

    /// <summary>The destination identity for this delivery.</summary>
    public required ChannelIdentity DestinationIdentity { get; init; }

    /// <summary>True when this delivery is on the same channel the request originated on.</summary>
    public bool Originating { get; init; }

    /// <summary>True when this is the echo push of the user input rather than the agent reply.</summary>
    public bool IsEcho { get; init; }
}