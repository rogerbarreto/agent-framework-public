// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Azure.AI.Projects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Dependency-injection helpers that register a <see cref="FoundryMemoryProvider"/> wired with a
/// built-in <see cref="HostedFoundryMemoryProviderScopes"/> strategy.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
public static class HostedFoundryMemoryProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="FoundryMemoryProvider"/> wired to the supplied
    /// <see cref="AIProjectClient"/> and a <see cref="HostedFoundryMemoryProviderScopes"/> helper
    /// selected by <paramref name="scope"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="client">The <see cref="AIProjectClient"/> used to talk to Foundry Memory.</param>
    /// <param name="memoryStoreName">The name of the memory store in Microsoft Foundry.</param>
    /// <param name="scope">The scope strategy. Defaults to <see cref="HostedFoundryMemoryScope.PerUser"/>.</param>
    /// <param name="options">Optional <see cref="FoundryMemoryProviderOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddHostedFoundryMemoryProvider(
        this IServiceCollection services,
        AIProjectClient client,
        string memoryStoreName,
        HostedFoundryMemoryScope scope = HostedFoundryMemoryScope.PerUser,
        FoundryMemoryProviderOptions? options = null)
    {
        Throw.IfNull(services);
        Throw.IfNull(client);
        Throw.IfNullOrWhitespace(memoryStoreName);

        services.AddSingleton(sp => new FoundryMemoryProvider(
            client,
            memoryStoreName,
            ResolveScopeInitializer(scope),
            options,
            sp.GetService<ILoggerFactory>()));
        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="FoundryMemoryProvider"/> that resolves its
    /// <see cref="AIProjectClient"/> from <see cref="IServiceProvider"/> at construction time.
    /// Use this overload when an <see cref="AIProjectClient"/> is already registered with the
    /// service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="memoryStoreName">The name of the memory store in Microsoft Foundry.</param>
    /// <param name="scope">The scope strategy. Defaults to <see cref="HostedFoundryMemoryScope.PerUser"/>.</param>
    /// <param name="options">Optional <see cref="FoundryMemoryProviderOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddHostedFoundryMemoryProvider(
        this IServiceCollection services,
        string memoryStoreName,
        HostedFoundryMemoryScope scope = HostedFoundryMemoryScope.PerUser,
        FoundryMemoryProviderOptions? options = null)
    {
        Throw.IfNull(services);
        Throw.IfNullOrWhitespace(memoryStoreName);

        services.AddSingleton(sp => new FoundryMemoryProvider(
            sp.GetRequiredService<AIProjectClient>(),
            memoryStoreName,
            ResolveScopeInitializer(scope),
            options,
            sp.GetService<ILoggerFactory>()));
        return services;
    }

    private static Func<AgentSession?, FoundryMemoryProvider.State> ResolveScopeInitializer(HostedFoundryMemoryScope scope) =>
        scope switch
        {
            HostedFoundryMemoryScope.PerUser => HostedFoundryMemoryProviderScopes.PerUser(),
            HostedFoundryMemoryScope.PerChat => HostedFoundryMemoryProviderScopes.PerChat(),
            HostedFoundryMemoryScope.PerUserAndChat => HostedFoundryMemoryProviderScopes.PerUserAndChat(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, $"Unknown {nameof(HostedFoundryMemoryScope)} value.")
        };
}
