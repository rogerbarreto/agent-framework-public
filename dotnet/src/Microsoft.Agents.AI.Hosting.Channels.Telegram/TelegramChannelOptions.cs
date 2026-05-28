// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.AI.Hosting.Channels.Telegram;

/// <summary>
/// Configuration for <see cref="TelegramChannel"/>.
/// </summary>
public sealed class TelegramChannelOptions
{
    /// <summary>Bot token issued by Telegram's BotFather. Required.</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>How the channel receives inbound updates. Default <see cref="TelegramTransport.Polling"/>.</summary>
    public TelegramTransport Transport { get; set; } = TelegramTransport.Polling;

    /// <summary>
    /// Mount root for channel routes. Default <c>"/telegram"</c>. Webhook transport publishes
    /// <c>{Path}/webhook</c>; polling transport publishes no HTTP routes.
    /// </summary>
    public string Path { get; set; } = "/telegram";

    /// <summary>How to derive the host isolation key in multi-user conversations. Default <see cref="ConversationScope.PerUserPerConversation"/>.</summary>
    public ConversationScope ConversationScope { get; set; } = ConversationScope.PerUserPerConversation;

    /// <summary>Group-conversation acceptance filter. Default <see cref="AcceptInGroup.MentionOnly"/>.</summary>
    public AcceptInGroup AcceptInGroup { get; set; } = AcceptInGroup.MentionOnly;

    /// <summary>Whether to force a link ceremony on every inbound message. Default <see langword="false"/>.</summary>
    public bool RequireLink { get; set; }

    /// <summary>Declarative channel commands; the host calls <c>setMyCommands</c> at startup when <see cref="RegisterNativeCommands"/> is <see langword="true"/>.</summary>
    public IList<ChannelCommand> Commands { get; } = [];

    /// <summary>Whether the channel registers <see cref="Commands"/> with Telegram via <c>setMyCommands</c> on startup. Default <see langword="true"/>.</summary>
    public bool RegisterNativeCommands { get; set; } = true;

    /// <summary>Polling interval when <see cref="Transport"/> is <see cref="TelegramTransport.Polling"/>. Default 25 seconds (Telegram's long-poll cap).</summary>
    public TimeSpan PollingTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>Optional run hook invoked after the channel produces the default request.</summary>
    public IChannelRunHook? RunHook { get; set; }

    /// <summary>Optional response hook invoked per delivery.</summary>
    public IChannelResponseHook? ResponseHook { get; set; }
}