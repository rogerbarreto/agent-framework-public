// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Handed to <see cref="Channel.Contribute"/>. Exposes the host's run / stream surface plus the host and
/// its state store.
/// </summary>
public interface IChannelContext
{
    /// <summary>Application service provider.</summary>
    IServiceProvider Services { get; }

    /// <summary>The host this channel was added to.</summary>
    AgentFrameworkHost Host { get; }

    /// <summary>The host state store.</summary>
    IHostStateStore StateStore { get; }

    /// <summary>Run the host target and return the (non-streaming) result.</summary>
    ValueTask<HostedRunResult> RunAsync(ChannelRequest request, CancellationToken cancellationToken = default);

    /// <summary>Stream the host target's response as <see cref="HostedStreamItem"/> envelopes.</summary>
    IAsyncEnumerable<HostedStreamItem> StreamAsync(ChannelRequest request, CancellationToken cancellationToken = default);
}
