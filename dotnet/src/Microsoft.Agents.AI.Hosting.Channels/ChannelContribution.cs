// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Returned by <see cref="Channel.Contribute"/>. Carries the routes, commands, endpoint filters,
/// and lifecycle hooks the channel publishes to the running host.
/// </summary>
public sealed record ChannelContribution
{
    /// <summary>
    /// Route registration actions. The host invokes each one with an <see cref="IEndpointRouteBuilder"/>
    /// rooted at <see cref="Channel.Path"/> via <see cref="EndpointRoutingApplicationBuilderExtensions"/>'
    /// group semantics, so map paths relative to <see cref="Channel.Path"/>.
    /// </summary>
    public IReadOnlyList<Action<IEndpointRouteBuilder>> Routes { get; init; } = [];

    /// <summary>
    /// Endpoint filters applied to the <see cref="Channel.Path"/>-rooted group. Replaces Python's
    /// <c>middleware</c> slot.
    /// </summary>
    public IReadOnlyList<IEndpointFilter> EndpointFilters { get; init; } = [];

    /// <summary>Declarative commands; channels read these and call the protocol's native registration.</summary>
    public IReadOnlyList<ChannelCommand> Commands { get; init; } = [];

    /// <summary>Optional startup hook invoked once after DI is built. Useful for long-poll loops.</summary>
    public Func<CancellationToken, ValueTask>? OnStartup { get; init; }

    /// <summary>Optional shutdown hook invoked during graceful shutdown.</summary>
    public Func<CancellationToken, ValueTask>? OnShutdown { get; init; }
}
