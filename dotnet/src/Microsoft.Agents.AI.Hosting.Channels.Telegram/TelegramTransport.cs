// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels.Telegram;

/// <summary>How the channel receives inbound updates from Telegram.</summary>
public enum TelegramTransport
{
    /// <summary>Long-poll <c>getUpdates</c> from an <see cref="Microsoft.Extensions.Hosting.IHostedService"/>.</summary>
    Polling,

    /// <summary>Receive HTTP POSTs at <c>{Path}/webhook</c>. The bot's webhook URL must be registered out-of-band.</summary>
    Webhook,
}