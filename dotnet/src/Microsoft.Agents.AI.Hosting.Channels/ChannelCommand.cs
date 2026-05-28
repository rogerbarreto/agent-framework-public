// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Declarative description of a channel-native command (Telegram slash command, Discord
/// application command, ...). Channels emit these from <see cref="Channel.Contribute"/> as
/// passive metadata; native registration with the protocol is the channel's responsibility.
/// </summary>
/// <param name="Name">The command name without any leading sentinel (e.g. "new" not "/new").</param>
/// <param name="Description">Short description surfaced in the protocol's UI.</param>
public sealed record ChannelCommand(string Name, string Description);