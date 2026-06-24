// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

internal sealed class ChannelContext : IChannelContext
{
    public ChannelContext(IServiceProvider services, AgentFrameworkHost host)
    {
        this.Services = Throw.IfNull(services);
        this.Host = Throw.IfNull(host);
    }

    public IServiceProvider Services { get; }
    public AgentFrameworkHost Host { get; }
    public IHostStateStore StateStore => this.Host.StateStore;

    public ValueTask<HostedRunResult> RunAsync(ChannelRequest request, CancellationToken cancellationToken = default)
        => this.Host.RunAsync(request, cancellationToken);

    public IAsyncEnumerable<HostedStreamItem> StreamAsync(ChannelRequest request, CancellationToken cancellationToken = default)
        => this.Host.StreamAsync(request, cancellationToken);
}
