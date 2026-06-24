// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Context passed to a channel command handler when the host dispatches a recognized
/// <see cref="ChannelCommand"/>. Carries the originating request and the parsed argument string.
/// </summary>
/// <param name="Request">The originating channel request.</param>
/// <param name="Command">The matched command.</param>
/// <param name="Arguments">The raw argument text following the command, or <see langword="null"/>.</param>
public sealed record ChannelCommandContext(ChannelRequest Request, ChannelCommand Command, string? Arguments);
