// Copyright (c) Microsoft. All rights reserved.

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Extension methods for registering the protocol-neutral parts of a Foundry Hosted Agent on an
/// <see cref="IHostApplicationBuilder"/>.
/// </summary>
public static class HostApplicationBuilderExtensions
{
    /// <summary>
    /// Registers the <see cref="AgentSessionStore"/> that Foundry hosting uses to load and save the hosted
    /// agent's sessions, on a host application builder.
    /// </summary>
    /// <remarks>
    /// The store registered last is used, as described for
    /// <see cref="ServiceCollectionExtensions.AddFoundryAgentSessionStore(IServiceCollection, AgentSessionStore)"/>.
    /// The DI container never disposes <paramref name="agentSessionStore"/>. Each store keeps its own
    /// sessions, so changing the store means sessions saved in the previous store are no longer found.
    /// <code>
    /// builder.AddFoundryAgentSessionStore(mySessionStore);
    /// </code>
    /// </remarks>
    /// <param name="host">The host application builder.</param>
    /// <param name="agentSessionStore">The session store to use.</param>
    /// <returns>The host application builder for chaining.</returns>
    public static IHostApplicationBuilder AddFoundryAgentSessionStore(
        this IHostApplicationBuilder host,
        AgentSessionStore agentSessionStore)
    {
        _ = Throw.IfNull(host);
        host.Services.AddFoundryAgentSessionStore(agentSessionStore);
        return host;
    }

    /// <summary>
    /// Registers a factory for the <see cref="AgentSessionStore"/> that Foundry hosting uses to load and save
    /// the hosted agent's sessions, with the specified DI lifetime, on a host application builder.
    /// </summary>
    /// <remarks>
    /// The protocol handler looks the store up once per run, from the DI scope of that run. The store's
    /// lifetime is independent of the agent's lifetime. The store registered last is used, as described for
    /// <see cref="ServiceCollectionExtensions.AddFoundryAgentSessionStore(IServiceCollection, Func{IServiceProvider, AgentSessionStore}, ServiceLifetime)"/>.
    /// <code>
    /// builder.AddFoundryAgentSessionStore(
    ///     sp => new MySessionStore(sp.GetRequiredService&lt;MyDbContext&gt;()),
    ///     ServiceLifetime.Scoped);
    /// </code>
    /// </remarks>
    /// <param name="host">The host application builder.</param>
    /// <param name="agentSessionStoreFactory">Creates the session store from the service provider of the current DI scope.</param>
    /// <param name="lifetime">The DI lifetime of the session store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The host application builder for chaining.</returns>
    public static IHostApplicationBuilder AddFoundryAgentSessionStore(
        this IHostApplicationBuilder host,
        Func<IServiceProvider, AgentSessionStore> agentSessionStoreFactory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        _ = Throw.IfNull(host);
        host.Services.AddFoundryAgentSessionStore(agentSessionStoreFactory, lifetime);
        return host;
    }
}
