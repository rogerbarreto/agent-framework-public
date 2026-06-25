// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Declarative description of a channel-native command (Telegram slash command, Discord
/// application command, ...). Channels emit these from <see cref="Channel.Contribute"/> as
/// passive metadata; native registration with the protocol is the channel's responsibility.
/// </summary>
public sealed class ChannelCommand
{
    /// <summary>Gets the command name without any leading sentinel (e.g. "new" not "/new").</summary>
    public string Name { get; }

    /// <summary>Gets the short description surfaced in the protocol's UI.</summary>
    public string Description { get; }

    /// <summary>Initializes a new instance of <see cref="ChannelCommand"/>.</summary>
    /// <param name="name">The command name without any leading sentinel (e.g. "new" not "/new").</param>
    /// <param name="description">Short description surfaced in the protocol's UI.</param>
    public ChannelCommand(string name, string description)
    {
        this.Name = name;
        this.Description = description;
    }
}
