// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Base type for items yielded by <see cref="IChannelContext.StreamAsync"/>.
/// Covers both normalized agent updates (<see cref="HostedStreamUpdate"/>) and protocol-specific
/// events (<see cref="HostedStreamEvent"/>) behind one stream type.
/// </summary>
/// <remarks>
/// <see cref="HostedStreamCompleted"/> is always the terminal item and carries the final
/// <see cref="HostedRunResult"/> for downstream bookkeeping (intended-targets envelope,
/// durable push scheduling).
/// </remarks>
public abstract record HostedStreamItem
{
    private protected HostedStreamItem() { }
}

/// <summary>Normalized agent stream update; lossless for messages, function calls, usage.</summary>
public sealed record HostedStreamUpdate(AgentResponseUpdate Update) : HostedStreamItem;

/// <summary>Protocol-specific event the framework does not model (workflow events, AG-UI events).</summary>
public sealed record HostedStreamEvent(object Event) : HostedStreamItem;

/// <summary>Terminal item carrying the final result for post-stream bookkeeping.</summary>
public sealed record HostedStreamCompleted(HostedRunResult Result) : HostedStreamItem;
