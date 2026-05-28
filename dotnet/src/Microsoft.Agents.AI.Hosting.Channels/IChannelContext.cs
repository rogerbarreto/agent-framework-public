// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Handed to <see cref="Channel.Contribute"/>. Exposes the host's run / stream / authorization
/// surface plus the persisted state store and durable task runner.
/// </summary>
public interface IChannelContext
{
    /// <summary>Application service provider.</summary>
    IServiceProvider Services { get; }

    /// <summary>The host this channel was added to.</summary>
    AgentFrameworkHost Host { get; }

    /// <summary>The host state store.</summary>
    IHostStateStore StateStore { get; }

    /// <summary>The durable task runner that backs non-originating response delivery and background runs.</summary>
    IDurableTaskRunner DurableRunner { get; }

    /// <summary>
    /// Funnel a channel-native identity through the host's authorization pipeline.
    /// </summary>
    ValueTask<AuthorizationOutcome> AuthorizeAsync(
        ChannelIdentity identity,
        AuthorizationRequest options,
        CancellationToken cancellationToken = default);

    /// <summary>Run the host target with the given request and return the (non-streaming) result.</summary>
    ValueTask<HostedRunResult> RunAsync(ChannelRequest request, CancellationToken cancellationToken = default);

    /// <summary>Stream the host target's response as <see cref="HostedStreamItem"/> envelopes.</summary>
    IAsyncEnumerable<HostedStreamItem> StreamAsync(ChannelRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule outbound delivery for every non-originating destination resolved against
    /// <see cref="ChannelRequest.ResponseTarget"/>. Originating delivery is NOT scheduled here;
    /// channels render their own originating reply synchronously.
    /// </summary>
    ValueTask<IReadOnlyList<TaskHandle>> ScheduleResponseAsync(
        HostedRunResult result,
        ChannelRequest originating,
        CancellationToken cancellationToken = default);
}