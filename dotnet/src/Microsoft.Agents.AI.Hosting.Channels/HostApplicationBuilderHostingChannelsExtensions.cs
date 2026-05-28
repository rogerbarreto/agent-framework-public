// Copyright (c) Microsoft. All rights reserved.

using System;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.Channels;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods on <see cref="IHostApplicationBuilder"/> for composing an
/// <see cref="AgentFrameworkHost"/> with channels.
/// </summary>
public static class HostApplicationBuilderHostingChannelsExtensions
{
    /// <summary>Adds an agent-framework host whose target is the supplied <see cref="AIAgent"/>.</summary>
    public static IAgentFrameworkHostBuilder AddAgentFrameworkHost(
        this IHostApplicationBuilder builder,
        AIAgent target,
        Action<AgentFrameworkHostOptions>? configure = null)
    {
        Throw.IfNull(builder);
        Throw.IfNull(target);
        return AddAgentFrameworkHostCore(builder, configure, services => services.TryAddSingleton<IHostedTargetRunner>(_ => new AIAgentRunner(target)));
    }

    /// <summary>Adds an agent-framework host whose target is the supplied <see cref="Workflow"/>.</summary>
    public static IAgentFrameworkHostBuilder AddAgentFrameworkHost(
        this IHostApplicationBuilder builder,
        Workflow target,
        Action<AgentFrameworkHostOptions>? configure = null)
    {
        Throw.IfNull(builder);
        Throw.IfNull(target);
        return AddAgentFrameworkHostCore(builder, configure, services => services.TryAddSingleton<IHostedTargetRunner>(_ => new WorkflowRunner(target)));
    }

    /// <summary>
    /// Adds an agent-framework host whose target is resolved from a factory. Generic overload for
    /// alternative runners (Foundry, mocks, ...) supplied by other packages.
    /// </summary>
    public static IAgentFrameworkHostBuilder AddAgentFrameworkHost<TTarget>(
        this IHostApplicationBuilder builder,
        Func<IServiceProvider, TTarget> targetFactory,
        Action<AgentFrameworkHostOptions>? configure = null)
        where TTarget : class
    {
        Throw.IfNull(builder);
        Throw.IfNull(targetFactory);
        return AddAgentFrameworkHostCore(builder, configure, services => services.TryAddSingleton<TTarget>(targetFactory));
    }

    private static AgentFrameworkHostBuilder AddAgentFrameworkHostCore(
        IHostApplicationBuilder builder,
        Action<AgentFrameworkHostOptions>? configure,
        Action<IServiceCollection> registerTarget)
    {
        var options = new AgentFrameworkHostOptions();
        configure?.Invoke(options);

        var services = builder.Services;

        services.TryAddSingleton<IHostStateStore>(_ => new InMemoryHostStateStore());
        services.TryAddSingleton<InProcessDurableTaskRunner>();
        services.TryAddSingleton<IDurableTaskRunner>(sp => sp.GetRequiredService<InProcessDurableTaskRunner>());
        services.AddHostedService(sp => sp.GetRequiredService<InProcessDurableTaskRunner>());

        services.TryAddSingleton<ILinkPolicy>(_ => options.LinkPolicy ?? AllowAllLinkPolicy.Instance);
        services.TryAddSingleton<IIsolationKeysAccessor, IsolationKeysAccessor>();

        if (options.DefaultAllowlist is not null)
        {
            services.TryAddSingleton(options.DefaultAllowlist);
        }

        registerTarget(services);

        services.TryAddSingleton(options);
        services.TryAddSingleton<AgentFrameworkHost>(sp => new AgentFrameworkHost(
            sp,
            sp.GetRequiredService<IHostedTargetRunner>(),
            sp.GetRequiredService<System.Collections.Generic.IReadOnlyList<Channel>>(),
            sp.GetRequiredService<IHostStateStore>(),
            sp.GetRequiredService<IDurableTaskRunner>(),
            sp.GetRequiredService<AgentFrameworkHostOptions>()));

        return new AgentFrameworkHostBuilder(services, options);
    }
}