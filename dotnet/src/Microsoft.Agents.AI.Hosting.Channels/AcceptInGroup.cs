// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Controls which inbound group-conversation messages a channel accepts.
/// </summary>
public enum AcceptInGroup
{
    /// <summary>Accept only messages that mention the bot. Default for group surfaces.</summary>
    MentionOnly,

    /// <summary>Accept only registered <see cref="ChannelCommand"/> invocations.</summary>
    CommandOnly,

    /// <summary>Accept either mentions or commands.</summary>
    MentionOrCommand,

    /// <summary>Accept every inbound message. Opt-in for groups where the bot is the only conversational participant.</summary>
    All,
}