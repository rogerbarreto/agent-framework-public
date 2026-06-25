// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Context passed to a channel command handler when the host dispatches a recognized
/// <see cref="ChannelCommand"/>. Carries the originating request and the parsed argument string.
/// </summary>
public sealed class ChannelCommandContext
{
    /// <summary>Gets the originating channel request.</summary>
    public ChannelRequest Request { get; }

    /// <summary>Gets the matched command.</summary>
    public ChannelCommand Command { get; }

    /// <summary>Gets the raw argument text following the command, or <see langword="null"/>.</summary>
    public string? Arguments { get; }

    /// <summary>Initializes a new instance of <see cref="ChannelCommandContext"/>.</summary>
    /// <param name="request">The originating channel request.</param>
    /// <param name="command">The matched command.</param>
    /// <param name="arguments">The raw argument text following the command, or <see langword="null"/>.</param>
    public ChannelCommandContext(ChannelRequest request, ChannelCommand command, string? arguments)
    {
        this.Request = request;
        this.Command = command;
        this.Arguments = arguments;
    }
}
