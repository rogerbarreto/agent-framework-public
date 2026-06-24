// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Context passed to <see cref="IChannelResponseHook.OnResponseAsync"/>. Runs after target invocation and
/// before the originating channel serializes its response.
/// </summary>
public sealed record ChannelResponseContext
{
    /// <summary>The originating request.</summary>
    public required ChannelRequest Request { get; init; }

    /// <summary>The originating channel name.</summary>
    public required string ChannelName { get; init; }
}
