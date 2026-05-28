// Copyright (c) Microsoft. All rights reserved.

using System;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels.Telegram;

/// <summary>
/// Extensions on <see cref="IAgentFrameworkHostBuilder"/> for the Telegram channel.
/// </summary>
public static class AgentFrameworkHostBuilderTelegramExtensions
{
    /// <summary>Add the Telegram channel.</summary>
    public static IAgentFrameworkHostBuilder AddTelegramChannel(
        this IAgentFrameworkHostBuilder builder,
        Action<TelegramChannelOptions> configure)
    {
        Throw.IfNull(builder);
        Throw.IfNull(configure);
        var options = new TelegramChannelOptions();
        configure(options);
        return builder.AddChannel(new TelegramChannel(options));
    }
}