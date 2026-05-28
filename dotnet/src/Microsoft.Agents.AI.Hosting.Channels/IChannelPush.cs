// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Capability interface: a channel can deliver a response to a destination identity it owns.
/// Implementations are invoked by the host's <c>hosting.push</c> durable task handler.
/// </summary>
public interface IChannelPush
{
    /// <summary>Push a result to the destination described by <paramref name="context"/>.</summary>
    ValueTask PushAsync(ChannelPushContext context, HostedRunResult payload, CancellationToken cancellationToken);
}