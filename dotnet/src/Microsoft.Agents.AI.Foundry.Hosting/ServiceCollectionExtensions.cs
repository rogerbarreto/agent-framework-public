// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Extension methods for registering the protocol-neutral parts of a Foundry Hosted Agent: the agent session
/// store and the Foundry Toolboxes integration.
/// </summary>
/// <remarks>
/// A protocol package registers the endpoints that serve the agent, for example <c>AddFoundryResponses</c> in
/// <c>Microsoft.Agents.AI.Foundry.Hosting.Responses</c>. The registrations here are shared by every protocol.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="AgentSessionStore"/> that Foundry hosting uses to load and save the hosted
    /// agent's sessions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store registered last is used. This call replaces a store registered earlier, including one passed
    /// to <c>AddFoundryResponses(agent, agentSessionStore)</c> or registered directly as an
    /// <see cref="AgentSessionStore"/>; a later such registration replaces this one. <c>AddFoundryResponses</c>
    /// without a store does not replace a store registered earlier. Without any store, the default store is
    /// used, which uses the AgentServer SDK's local state-store fallback outside Foundry. Each store keeps its
    /// own sessions, so changing the store means sessions saved in the previous store are no longer found.
    /// </para>
    /// <para>
    /// The DI container never disposes <paramref name="agentSessionStore"/>, because the caller created it.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// builder.Services.AddFoundryAgentSessionStore(mySessionStore);
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="agentSessionStore">The session store to use.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFoundryAgentSessionStore(
        this IServiceCollection services,
        AgentSessionStore agentSessionStore)
    {
        _ = Throw.IfNull(services);
        _ = Throw.IfNull(agentSessionStore);

        RemoveAgentSessionStoreRegistrations(services);
        services.AddSingleton(agentSessionStore);
        return services;
    }

    /// <summary>
    /// Registers a factory for the <see cref="AgentSessionStore"/> that Foundry hosting uses to load and save
    /// the hosted agent's sessions, with the specified DI lifetime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For each run the protocol handler looks the store up once, from the DI scope of that run. The store's
    /// lifetime is independent of the agent's lifetime:
    /// <list type="bullet">
    /// <item><description><see cref="ServiceLifetime.Singleton"/>: one instance for the process, disposed at shutdown.</description></item>
    /// <item><description><see cref="ServiceLifetime.Scoped"/>: one instance per run, disposed when the run ends.</description></item>
    /// <item><description><see cref="ServiceLifetime.Transient"/>: a new instance on every lookup, disposed when the run ends.</description></item>
    /// </list>
    /// The DI container disposes what the factory returns according to that lifetime, even when the factory
    /// returns an object it did not create. To share an object you dispose yourself, use
    /// <see cref="AddFoundryAgentSessionStore(IServiceCollection, AgentSessionStore)"/>.
    /// </para>
    /// <para>
    /// The store registered last is used, as described for
    /// <see cref="AddFoundryAgentSessionStore(IServiceCollection, AgentSessionStore)"/>. Each store keeps its own
    /// sessions, so changing the store means sessions saved in the previous store are no longer found.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// builder.Services.AddFoundryAgentSessionStore(
    ///     sp => new MySessionStore(sp.GetRequiredService&lt;MyDbContext&gt;()),
    ///     ServiceLifetime.Scoped);
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="agentSessionStoreFactory">Creates the session store from the service provider of the current DI scope.</param>
    /// <param name="lifetime">The DI lifetime of the session store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFoundryAgentSessionStore(
        this IServiceCollection services,
        Func<IServiceProvider, AgentSessionStore> agentSessionStoreFactory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        _ = Throw.IfNull(services);
        _ = Throw.IfNull(agentSessionStoreFactory);
        FoundryHostedAgentSource.ValidateLifetime(lifetime);

        AgentSessionStore CreateStore(IServiceProvider serviceProvider) =>
            agentSessionStoreFactory(serviceProvider)
                ?? throw new InvalidOperationException("The agent session store factory returned null.");

        RemoveAgentSessionStoreRegistrations(services);
        services.Add(new ServiceDescriptor(typeof(AgentSessionStore), CreateStore, lifetime));
        return services;
    }

    /// <summary>
    /// Describes the source of an agent instance the caller owns. The instance registration is never
    /// disposed by the container, matching that the agent is not either.
    /// </summary>
    internal static IReadOnlyList<ServiceDescriptor> DescribeAgentSource(AIAgent agent) =>
        [ServiceDescriptor.Singleton(new FoundryHostedAgentSource(agent))];

    /// <summary>
    /// Describes the source of an agent created by <paramref name="agentFactory"/>. The source is created by
    /// the container, which disposes it, and with it a singleton agent, at shutdown.
    /// </summary>
    internal static IReadOnlyList<ServiceDescriptor> DescribeAgentSource(
        Func<IServiceProvider, AIAgent> agentFactory,
        ServiceLifetime lifetime)
    {
        _ = Throw.IfNull(agentFactory);
        FoundryHostedAgentSource.ValidateLifetime(lifetime);

        ServiceDescriptor source = ServiceDescriptor.Singleton(serviceProvider => new FoundryHostedAgentSource(
            agentFactory,
            lifetime,
            serviceProvider));

        if (lifetime != ServiceLifetime.Singleton)
        {
            return [source];
        }

        ServiceDescriptor owner = ServiceDescriptor.Singleton(serviceProvider =>
            new FoundryHostedAgentSource.FoundryHostedSingletonAgentOwner(
                agentFactory(serviceProvider)
                    ?? throw new InvalidOperationException("The agent factory passed to AddFoundryResponses returned null.")));

        return [owner, source];
    }

    /// <summary>
    /// Removes the <see cref="AgentSessionStore"/> registrations, so the store registered next replaces them.
    /// </summary>
    private static void RemoveAgentSessionStoreRegistrations(IServiceCollection services)
    {
        for (int i = services.Count - 1; i >= 0; i--)
        {
            ServiceDescriptor descriptor = services[i];
            if (descriptor.ServiceType == typeof(AgentSessionStore) && !descriptor.IsKeyedService)
            {
                services.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Registers the Foundry Toolbox service, which opens the configured Foundry toolboxes through the
    /// Toolboxes MCP proxy for each run and provides their MCP tools to the registered protocol handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each string in <paramref name="toolboxNames"/> is a toolbox name registered in the Foundry
    /// project. The proxy URL per toolbox is constructed as:
    /// <c>{FOUNDRY_PROJECT_ENDPOINT}/toolboxes/{toolboxName}/mcp?api-version=v1</c>
    /// </para>
    /// <para>
    /// The endpoint can also come from a configured override or the
    /// <c>AZURE_AI_PROJECT_ENDPOINT</c> local-development fallback. If no endpoint is resolved,
    /// startup completes with toolbox support disabled.
    /// A toolbox name that would not resolve to a single path segment of the proxy URL fails startup.
    /// </para>
    /// <para>
    /// Startup opens no connection. Each run opens its own MCP session per toolbox under its platform call id,
    /// discovers the tools available to its caller, and ends every session when it completes. A toolbox that
    /// needs the caller's OAuth consent stops the turn, and the protocol handler asks the caller for consent,
    /// for example with an <c>oauth_consent_request</c> output item on the Responses protocol. A toolbox that
    /// cannot be opened is skipped for that run.
    /// </para>
    /// <para>
    /// This method does not register a toolbox health check. Toolbox connectivity is evaluated
    /// when each run opens its connections, not by readiness probes.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// builder.Services.AddFoundryToolboxService(credential, "my-toolbox", "another-toolbox");
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="credential">The <see cref="TokenCredential"/> used to authenticate with the Foundry Toolboxes MCP proxy.</param>
    /// <param name="toolboxNames">Names of the Foundry toolboxes to connect to.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFoundryToolboxService(
        this IServiceCollection services,
        TokenCredential credential,
        params string[] toolboxNames)
        => services.AddFoundryToolboxService(credential, options: null, toolboxNames);

    /// <summary>
    /// Registers the Foundry Toolbox service with optional behavior configuration.
    /// </summary>
    /// <remarks>
    /// Toolbox startup and per-run connection behavior follow
    /// <see cref="AddFoundryToolboxService(IServiceCollection, TokenCredential, string[])"/>.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="credential">The <see cref="TokenCredential"/> used to authenticate with the Foundry Toolboxes MCP proxy.</param>
    /// <param name="options">
    /// Optional <see cref="FoundryToolboxServiceOptions"/>, such as
    /// <see cref="FoundryToolboxServiceOptions.TokenScope"/>, or
    /// <see cref="FoundryToolboxServiceOptions.AllowedOAuthConsentOrigins"/>.
    /// </param>
    /// <param name="toolboxNames">Names of the Foundry toolboxes to open for each run.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFoundryToolboxService(
        this IServiceCollection services,
        TokenCredential credential,
        FoundryToolboxServiceOptions? options,
        params string[] toolboxNames)
    {
        _ = Throw.IfNull(services);
        _ = Throw.IfNull(credential);

        if (services.Any(d => d.ServiceType == typeof(FoundryToolboxService)))
        {
            throw new InvalidOperationException(
                $"{nameof(FoundryToolboxService)} is already registered. " +
                $"Call {nameof(AddFoundryToolboxService)} only once per service collection.");
        }

        FoundryToolboxServiceOptions configuredOptions = CopyFoundryToolboxServiceOptions(options, toolboxNames);
        // Register FoundryToolboxService as a singleton, injecting the caller-provided credential
        // directly rather than resolving TokenCredential from DI.
        services.AddSingleton(sp => new FoundryToolboxService(
            configuredOptions,
            credential: credential,
            sp.GetService<ILogger<FoundryToolboxService>>()));
        services.AddHostedService(sp => sp.GetRequiredService<FoundryToolboxService>());

        MarkFeatureUsed(FeatureIndex.FoundryToolbox);
        return services;
    }

    private static FoundryToolboxServiceOptions CopyFoundryToolboxServiceOptions(
        FoundryToolboxServiceOptions? source,
        string[] toolboxNames)
    {
        var copy = new FoundryToolboxServiceOptions();
        if (source is not null)
        {
            copy.TokenScope = source.TokenScope;
            copy.AllowedOAuthConsentOrigins = source.AllowedOAuthConsentOrigins is null
                ? null
                : new HashSet<Uri>(source.AllowedOAuthConsentOrigins);
        }

        foreach (string name in toolboxNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                copy.ToolboxNames.Add(name);
            }
        }

        return copy;
    }

    internal static void MarkFeatureUsed(FeatureIndex feature)
    {
#pragma warning disable MAAI001
        FeatureUsage.MarkUsed((int)feature);
#pragma warning restore MAAI001
    }

    /// <summary>
    /// Configuration key the Foundry hosting platform populates with a non-empty value inside a
    /// hosted container. It is the documented way for container code to detect a Foundry context.
    /// </summary>
    internal const string FoundryHostingEnvironmentKey = "FOUNDRY_HOSTING_ENVIRONMENT";

    /// <summary>
    /// Configuration key holding the HTTP listen port, matching the Agent Server SDK.
    /// </summary>
    internal const string ListenPortKey = "PORT";

    /// <summary>
    /// Port the Foundry hosted runtime probes and routes to when <see cref="ListenPortKey"/> is
    /// not set, matching <see cref="FoundryEnvironment.Port"/>.
    /// </summary>
    internal const int DefaultListenPort = 8088;

    /// <summary>
    /// Creates the <see cref="AgentSessionStore"/> used when the caller did not supply one.
    /// </summary>
    /// <remarks>
    /// The AgentServer SDK selects the backend. Inside a Foundry container it uses the platform's
    /// durable state store, which survives replacement and is readable by every instance. Anywhere
    /// else it uses the SDK's local state-store fallback under <c>~/.agentserver/state_stores</c>.
    /// </remarks>
    internal static FoundryAgentSessionStore CreateDefaultAgentSessionStore() =>
        new(credential: CreateStateStoreCredential());

    /// <summary>
    /// Configures the URL used by a plain <c>WebApplication.CreateBuilder</c> host (Tier 3) so
    /// it listens on the port the Foundry hosted runtime probes and routes to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The URL is configured only when configuration reports a Foundry container through
    /// <see cref="FoundryHostingEnvironmentKey"/>. Outside Foundry the host keeps its configured
    /// addresses.
    /// </para>
    /// <para>
    /// Both values come from <see cref="IConfiguration"/> rather than from
    /// <see cref="FoundryEnvironment"/>, which caches every value in a static constructor. Reading
    /// through configuration keeps the decision observable when the host is built, honours the
    /// host's configuration sources, and lets tests supply values without mutating the process
    /// environment.
    /// </para>
    /// <para>
    /// Inside a Foundry container this replaces the URL inherited from the .NET base image. When
    /// <c>AgentHostBuilder</c> is used, its code-configured Kestrel listener takes precedence over
    /// this URL, so both hosting paths resolve to one listener without identifying the builder
    /// from its service registrations.
    /// </para>
    /// <para>
    /// The startup filter is resolved before ASP.NET reads the configured URLs and starts its
    /// server. Each protocol registration, such as <c>AddFoundryResponses</c>, runs once per service
    /// collection and registers it once.
    /// </para>
    /// </remarks>
    internal static void ConfigureFoundryListenPort(IServiceCollection services) =>
        services.AddSingleton<IStartupFilter, FoundryListenPortStartupFilter>();

    private sealed class FoundryListenPortStartupFilter : IStartupFilter
    {
        public FoundryListenPortStartupFilter(IConfiguration configuration)
        {
            // GenericWebHostService resolves startup filters before it reads this URL and starts
            // the server, while a code-configured AgentHost listener takes precedence over it.
            if (!string.IsNullOrEmpty(configuration[FoundryHostingEnvironmentKey]))
            {
                configuration[WebHostDefaults.ServerUrlsKey] =
                    $"http://+:{ResolveListenPort(configuration)}";
            }
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => next;
    }

    /// <summary>
    /// Reads the listen port from configuration, applying the same contract as
    /// <see cref="FoundryEnvironment.Port"/>: <see cref="DefaultListenPort"/> when unset, otherwise
    /// a port number in the range 1-65535.
    /// </summary>
    /// <exception cref="InvalidOperationException">The configured value is not a valid port.</exception>
    private static int ResolveListenPort(IConfiguration configuration)
    {
        var value = configuration[ListenPortKey];
        if (string.IsNullOrEmpty(value))
        {
            return DefaultListenPort;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"The {ListenPortKey} environment variable value '{value}' is not a valid port number (1-65535).");
        }

        return port;
    }

    /// <summary>
    /// Creates the credential required by the hosted state-store backend. The beta.29 SDK requires
    /// no credential for its local fallback, so local development does not construct one.
    /// </summary>
    internal static DefaultAzureCredential? CreateStateStoreCredential() =>
        FoundryEnvironment.IsHosted ? new DefaultAzureCredential() : null;
}
