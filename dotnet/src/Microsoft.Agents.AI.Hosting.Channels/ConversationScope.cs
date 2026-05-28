// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Controls how a channel derives the host isolation key in multi-user conversations.
/// </summary>
public enum ConversationScope
{
    /// <summary>One isolation key per user across all conversations. Personal-assistant style.</summary>
    PerUser,

    /// <summary>One isolation key per user per conversation. Default for multi-user surfaces.</summary>
    PerUserPerConversation,

    /// <summary>One isolation key per conversation. Every member shares state. "Bot lives in this channel" style.</summary>
    PerConversation,
}