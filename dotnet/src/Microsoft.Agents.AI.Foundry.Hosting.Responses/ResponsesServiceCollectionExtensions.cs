// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Azure.AI.AgentServer.Core.Tasks;
using Azure.AI.AgentServer.Responses;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Extension methods for registering agent-framework agents as Foundry Hosted Agents
/// using the Azure AI Responses Server SDK.
/// </summary>
public static class ResponsesServiceCollectionExtensions
{
    internal const string ResponsesServerSettingsSectionName = "ResponsesServer";

    /// <summary>
    /// Registers the Azure AI Responses Server SDK and the Agent Framework response handler that answers
    /// every request with <paramref name="agent"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Foundry publishes a dedicated endpoint for each hosted agent, so a container serves exactly one agent.
    /// Every request is answered by <paramref name="agent"/>: the request's <c>agent.name</c>, <c>model</c>, and
    /// <c>metadata["entity_id"]</c> do not select it, and no <see cref="AIAgent"/> registered in dependency
    /// injection is used. Call one <c>AddFoundryResponses</c> overload once per service collection; a second
    /// call throws.
    /// </para>
    /// <para>
    /// This overload is for local or non-hosted scenarios. Foundry hosted applications must call
    /// <see cref="ResponsesHostApplicationBuilderExtensions.AddFoundryResponses(IHostApplicationBuilder, AIAgent, AgentSessionStore?, FoundryResponsesServerOptions?)"/>
    /// so AgentServer can bind response and task storage from the host configuration.
    /// </para>
    /// <para>
    /// A non-hosted production platform owns caller authentication. It must remove any
    /// client-supplied <c>x-agent-user-id</c> header before injecting a verified identity.
    /// This package treats <c>ResponseContext.PlatformContext.UserIdKey</c> as trusted platform
    /// context; it does not authenticate that value. Directly exposing the endpoint while accepting
    /// a caller-provided identity for session isolation is unsupported.
    /// </para>
    /// <para>
    /// The agent is reused for every request and is never disposed by hosting, because the caller created it.
    /// To have hosting create the agent, per response run if needed, and dispose it, use
    /// <see cref="AddFoundryResponses(IServiceCollection, Func{IServiceProvider, AIAgent}, ServiceLifetime, FoundryResponsesServerOptions?)"/>.
    /// </para>
    /// <para>
    /// A store passed in <paramref name="agentSessionStore"/> is registered the same way
    /// <see cref="ServiceCollectionExtensions.AddFoundryAgentSessionStore(IServiceCollection, AgentSessionStore)"/> registers it, and the
    /// store registered last is used. Without one, the agent uses a store registered earlier, or otherwise the
    /// default store, which uses the AgentServer SDK's local state-store fallback outside Foundry. Each store
    /// keeps its own sessions, so changing the store means sessions saved in the previous store are no longer
    /// found.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// builder.Services.AddFoundryResponses(myAgent);
    ///
    /// var app = builder.Build();
    /// app.MapFoundryResponses();
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="agent">The agent that answers every request.</param>
    /// <param name="agentSessionStore">
    /// The session store for the agent. If null, the agent uses a store registered earlier, or otherwise the
    /// default store.
    /// </param>
    /// <param name="options">
    /// Optional <see cref="FoundryResponsesServerOptions"/>, for example to allow the
    /// agent's own service to store the responses it produces, or to opt in to durable long-running
    /// (resilient) background responses via <see cref="ResponsesServerOptions.ResilientBackground"/>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException"><c>AddFoundryResponses</c> was already called for <paramref name="services"/>.</exception>
    public static IServiceCollection AddFoundryResponses(
        this IServiceCollection services,
        AIAgent agent,
        AgentSessionStore? agentSessionStore = null,
        FoundryResponsesServerOptions? options = null)
    {
        _ = Throw.IfNull(services);
        _ = Throw.IfNull(agent);

        AddFoundryResponsesCore(
            services,
            host: null,
            ServiceCollectionExtensions.DescribeAgentSource(agent),
            agentSessionStore,
            CopyFoundryResponsesServerOptions(options),
            optionsSupplied: options is not null);
        return services;
    }

    /// <summary>
    /// Registers the Azure AI Responses Server SDK and the Agent Framework response handler that answers
    /// every request with an <see cref="AIAgent"/> created by <paramref name="agentFactory"/>, with the
    /// specified lifetime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this overload when the agent depends on services that are not singletons, for example a
    /// database context, or when it should not live for the whole process. The container serves exactly one
    /// agent, as described for
    /// <see cref="AddFoundryResponses(IServiceCollection, AIAgent, AgentSessionStore?, FoundryResponsesServerOptions?)"/>.
    /// The agent is never registered in dependency injection, but <paramref name="agentFactory"/> receives a
    /// service provider to resolve the agent's own dependencies from.
    /// </para>
    /// <para>
    /// Every response run (streaming, non-streaming, background, or a run resumed after a restart) gets
    /// its own DI scope, created when the run starts and disposed when its event stream ends. That is
    /// later than the end of the HTTP request for a background run. The lifetime applies to the agent:
    /// <list type="bullet">
    /// <item><description><see cref="ServiceLifetime.Singleton"/>: created on first use from the root service provider, reused for the process, and disposed at shutdown.</description></item>
    /// <item><description><see cref="ServiceLifetime.Scoped"/>: created from the run's scope once per response run, and disposed when the run ends.</description></item>
    /// <item><description><see cref="ServiceLifetime.Transient"/>: the same as <see cref="ServiceLifetime.Scoped"/>, because hosting is the only caller that gets the agent, once per run.</description></item>
    /// </list>
    /// Hosting disposes what the factory returns according to that lifetime, even when the factory returns an
    /// object it did not create. To serve an object you dispose yourself, use
    /// <see cref="AddFoundryResponses(IServiceCollection, AIAgent, AgentSessionStore?, FoundryResponsesServerOptions?)"/>.
    /// A readiness probe gets the agent the same way, so a scoped or transient agent is also created and
    /// disposed for each probe.
    /// </para>
    /// <para>
    /// Hosting adds an <see cref="OpenTelemetryAgent"/> to a singleton agent, unless it already has one, and
    /// disposes that wrapper at shutdown. For a scoped or transient agent, hosting adds and disposes a separate
    /// wrapper for every response after applying Foundry workflow checkpointing. A factory that supplies its own
    /// <see cref="OpenTelemetryAgent"/> must arrange the inner agent's lifetime independently. See
    /// <see cref="FoundryResponsesServerOptions.RequireOpenTelemetryAgent"/>.
    /// </para>
    /// <para>
    /// The run's scope is not the ASP.NET Core request scope, so scoped services that middleware fills in for
    /// an HTTP request are new, empty instances here. Do not derive the caller's identity from such services
    /// in the factory; the per-request user identity is supplied by the platform and applied by the response
    /// handler when it loads and saves sessions.
    /// </para>
    /// <para>
    /// The session store is registered separately with
    /// <see cref="ServiceCollectionExtensions.AddFoundryAgentSessionStore(IServiceCollection, Func{IServiceProvider, AgentSessionStore}, ServiceLifetime)"/>,
    /// with its own lifetime, because the response handler uses the store directly rather than through the
    /// agent. Without such a registration the agent uses the default store. Conversation state is not kept on
    /// the agent object: each turn loads the saved session from the store. Sessions are saved under the
    /// created agent's <see cref="AIAgent.Name"/>, the same location
    /// <see cref="AddFoundryResponses(IServiceCollection, AIAgent, AgentSessionStore?, FoundryResponsesServerOptions?)"/>
    /// uses, so the factory must give every agent it creates the same name, and switching between the two
    /// overloads or between lifetimes keeps existing sessions readable.
    /// </para>
    /// <para>
    /// This overload is for local or non-hosted scenarios. Foundry hosted applications must use
    /// the <see cref="IHostApplicationBuilder"/> overload.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// builder.Services.AddFoundryResponses(
    ///     sp => new MyAgent(sp.GetRequiredService&lt;MyDbContext&gt;()),
    ///     ServiceLifetime.Scoped);
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="agentFactory">Creates the agent from the service provider of the root or of the current response run.</param>
    /// <param name="lifetime">How long a created agent is reused. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <param name="options">
    /// Optional <see cref="FoundryResponsesServerOptions"/>, for example to allow the
    /// agent's own service to store the responses it produces, or to opt in to durable long-running
    /// (resilient) background responses via <see cref="ResponsesServerOptions.ResilientBackground"/>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException"><c>AddFoundryResponses</c> was already called for <paramref name="services"/>.</exception>
    public static IServiceCollection AddFoundryResponses(
        this IServiceCollection services,
        Func<IServiceProvider, AIAgent> agentFactory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton,
        FoundryResponsesServerOptions? options = null)
    {
        _ = Throw.IfNull(services);

        AddFoundryResponsesCore(
            services,
            host: null,
            ServiceCollectionExtensions.DescribeAgentSource(agentFactory, lifetime),
            agentSessionStore: null,
            CopyFoundryResponsesServerOptions(options),
            optionsSupplied: options is not null);
        return services;
    }

    /// <summary>
    /// Registers everything Foundry Responses hosting needs to serve the one agent described by
    /// <paramref name="agentSource"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="host">The host application builder, or <see langword="null"/> for a service collection alone.</param>
    /// <param name="agentSource">The registrations that supply and own the hosted agent.</param>
    /// <param name="agentSessionStore">The session store passed with the agent, if any.</param>
    /// <param name="configuredOptions">The hosting options to apply.</param>
    /// <param name="optionsSupplied">Whether the caller passed options, rather than leaving them to configuration.</param>
    /// <exception cref="InvalidOperationException"><c>AddFoundryResponses</c> was already called for <paramref name="services"/>.</exception>
    internal static void AddFoundryResponsesCore(
        IServiceCollection services,
        IHostApplicationBuilder? host,
        IReadOnlyList<ServiceDescriptor> agentSource,
        AgentSessionStore? agentSessionStore,
        FoundryResponsesServerOptions configuredOptions,
        bool optionsSupplied)
    {
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(FoundryHostedAgentSource)))
        {
            throw new InvalidOperationException(
                "AddFoundryResponses has already been called for this service collection. A Foundry hosted agent container serves exactly one agent, so call AddFoundryResponses once, with that agent or a factory for it.");
        }

        foreach (ServiceDescriptor descriptor in agentSource)
        {
            services.Add(descriptor);
        }

        AddResponsesServer(services, host, configuredOptions, optionsSupplied);
        services.AddHealthChecks();
        ServiceCollectionExtensions.ConfigureFoundryListenPort(services);
        ConfigureFoundryResponsesServerOptions(services, configuredOptions);
        if (agentSessionStore is not null)
        {
            services.AddFoundryAgentSessionStore(agentSessionStore);
        }
        else
        {
            services.TryAddSingleton<AgentSessionStore>(_ => ServiceCollectionExtensions.CreateDefaultAgentSessionStore());
        }

        RegisterResponseHandler(services);
        MarkFeatureUsed();
    }

    /// <summary>
    /// Copies the caller's <see cref="FoundryResponsesServerOptions"/>, so a later change to the caller's
    /// object does not change what hosting applies.
    /// </summary>
    internal static FoundryResponsesServerOptions CopyFoundryResponsesServerOptions(FoundryResponsesServerOptions? source)
    {
        if (source is null)
        {
            return new();
        }

        return new FoundryResponsesServerOptions
        {
            InferenceStoredOutputAllowed = source.InferenceStoredOutputAllowed,
            OAuthConsentInterruptedToolResult = source.OAuthConsentInterruptedToolResult,
            ExcludeReasoningEncryptedContent = source.ExcludeReasoningEncryptedContent,
            DefaultModel = source.DefaultModel,
            DefaultFetchHistoryCount = source.DefaultFetchHistoryCount,
            ResilientBackground = source.ResilientBackground,
            SteerableConversations = source.SteerableConversations,
            ResponseAcceptor = source.ResponseAcceptor,
            RequireOpenTelemetryAgent = source.RequireOpenTelemetryAgent,
        };
    }

    internal static void RegisterResponseHandler(IServiceCollection services)
    {
        services.TryAddSingleton<ResponseHandler>(serviceProvider =>
            new AgentFrameworkResponseHandler(
                serviceProvider,
                serviceProvider.GetRequiredService<FoundryHostedAgentSource>(),
                serviceProvider.GetRequiredService<ILogger<AgentFrameworkResponseHandler>>(),
                serviceProvider.GetRequiredService<IOptions<FoundryResponsesServerOptions>>().Value,
                serviceProvider.GetService<FoundryToolboxService>()));
    }

    /// <summary>
    /// Applies the hosting options and registers the readiness checks that report a misconfigured agent: one
    /// having its own service store the responses it produces, and a workflow agent writing its checkpoints
    /// somewhere hosting does not manage.
    /// </summary>
    /// <remarks>
    /// The checks are registered on the same <c>/readiness</c> pipeline that
    /// <see cref="ResponsesEndpointRouteBuilderExtensions.MapFoundryResponses"/> maps, so such a container never takes
    /// traffic. Resilience flags on <see cref="FoundryResponsesServerOptions"/> are forwarded to
    /// <see cref="ResponsesServerOptions"/> so the AgentServer SDK enables recovery for the same host.
    /// </remarks>
    internal static void ConfigureFoundryResponsesServerOptions(
        IServiceCollection services,
        FoundryResponsesServerOptions configuredOptions)
    {
        services.Configure<FoundryResponsesServerOptions>(options =>
        {
            options.InferenceStoredOutputAllowed = configuredOptions.InferenceStoredOutputAllowed;
            options.OAuthConsentInterruptedToolResult = configuredOptions.OAuthConsentInterruptedToolResult;
            options.ExcludeReasoningEncryptedContent = configuredOptions.ExcludeReasoningEncryptedContent;
            options.DefaultModel = configuredOptions.DefaultModel;
            options.DefaultFetchHistoryCount = configuredOptions.DefaultFetchHistoryCount;
            options.ResilientBackground = configuredOptions.ResilientBackground;
            options.SteerableConversations = configuredOptions.SteerableConversations;
            options.ResponseAcceptor = configuredOptions.ResponseAcceptor;
            options.RequireOpenTelemetryAgent = configuredOptions.RequireOpenTelemetryAgent;
        });

        AddReadinessCheck(
            services,
            "foundry-stored-output",
            sp => ActivatorUtilities.CreateInstance<HostedStoredOutputHealthCheck>(
                sp,
                sp.GetRequiredService<IOptions<FoundryResponsesServerOptions>>().Value));
        AddReadinessCheck(services, "foundry-workflow-checkpointing", sp => ActivatorUtilities.CreateInstance<HostedWorkflowCheckpointingHealthCheck>(sp));
    }

    private static void AddReadinessCheck(IServiceCollection services, string name, Func<IServiceProvider, IHealthCheck> factory) =>
        services.Configure<HealthCheckServiceOptions>(opts =>
            opts.Registrations.Add(new HealthCheckRegistration(
                name: name,
                factory: factory,
                failureStatus: HealthStatus.Unhealthy,
                tags: ["foundry", "responses", "readiness"])));

    /// <summary>
    /// Marks the Foundry hosting feature as used. Responses hosting owns this feature because it is the
    /// protocol that registers and maps the hosted agent.
    /// </summary>
    internal static void MarkFeatureUsed()
    {
#pragma warning disable MAAI001
        FeatureUsage.MarkUsed((int)Responses.FeatureIndex.FoundryHosting);
#pragma warning restore MAAI001
    }

    /// <summary>
    /// Registers the Responses Server SDK. <c>AddFoundryResponsesCore</c> runs once per service collection,
    /// which this relies on: <c>AddResponsesServer</c> registers a resilient task under a fixed name and
    /// throws when that name is already taken.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="host">The host application builder, or <see langword="null"/> for a service collection alone.</param>
    /// <param name="configuredOptions">The hosting options to apply.</param>
    /// <param name="optionsSupplied">
    /// Whether the caller passed options. When not, AgentServer binds its settings from the host configuration.
    /// </param>
    internal static void AddResponsesServer(
        IServiceCollection services,
        IHostApplicationBuilder? host,
        FoundryResponsesServerOptions configuredOptions,
        bool optionsSupplied)
    {
        if (host is not null)
        {
#pragma warning disable SCME0002
            host.AddResponsesServer(
                ResponsesServerSettingsSectionName,
                settings =>
                {
                    if (optionsSupplied)
                    {
                        settings.DefaultModel = configuredOptions.DefaultModel;
                        settings.DefaultFetchHistoryCount = configuredOptions.DefaultFetchHistoryCount;
                        settings.ResilientBackground = configuredOptions.ResilientBackground;
                        settings.SteerableConversations = configuredOptions.SteerableConversations;
                        settings.ResponseAcceptor = configuredOptions.ResponseAcceptor;
                    }
                });
#pragma warning restore SCME0002
        }
        else
        {
            services.AddResponsesServer(options =>
            {
                if (optionsSupplied)
                {
                    options.DefaultModel = configuredOptions.DefaultModel;
                    options.DefaultFetchHistoryCount = configuredOptions.DefaultFetchHistoryCount;
                    options.ResilientBackground = configuredOptions.ResilientBackground;
                    options.SteerableConversations = configuredOptions.SteerableConversations;
                    options.ResponseAcceptor = configuredOptions.ResponseAcceptor;
                }
            });
        }

        if (configuredOptions.SteerableConversations)
        {
            services.SetResilientTasksEnabled();
        }
    }

    /// <summary>
    /// The ActivitySource and Meter name for the Responses hosting pipeline, which the Foundry telemetry
    /// pipeline registered by <c>Azure.AI.AgentServer.Core</c> exports.
    /// </summary>
    internal const string ResponsesSourceName = "Azure.AI.AgentServer.Responses";

    /// <summary>
    /// Wraps <paramref name="agent"/> with <see cref="OpenTelemetryAgent"/> instrumentation
    /// so that agent invocations emit spans into the pipeline registered by
    /// <c>Azure.AI.AgentServer.Core</c>'s <c>AddAgentHostTelemetry()</c>.
    /// If the agent is already instrumented the original instance is returned unchanged.
    /// </summary>
    internal static AIAgent ApplyOpenTelemetry(AIAgent agent)
    {
        if (agent.GetService<OpenTelemetryAgent>() is not null)
        {
            return agent;
        }

        return agent.AsBuilder()
                    .UseOpenTelemetry(sourceName: ResponsesSourceName)
                    .Build();
    }

    /// <summary>
    /// Registers the hosted-agent <c>User-Agent</c> supplement policy
    /// (<see cref="HostedAgentUserAgentPolicy"/>) on the agent's underlying chat client via the
    /// MEAI 10.9.0 <see cref="OpenAIRequestPolicies"/> hook so every outgoing OpenAI Responses
    /// request carries the segment <c>foundry-hosting/agent-framework-dotnet/{version}</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Best-effort and idempotent. The method is a no-op when:
    /// <list type="bullet">
    /// <item><description><paramref name="agent"/> exposes no <see cref="IChatClient"/>;</description></item>
    /// <item><description>the chat client is not OpenAI-backed (the <see cref="OpenAIRequestPolicies"/> service lookup returns <see langword="null"/>);</description></item>
    /// <item><description>the policy was already registered on this client by a prior invocation (deduped via reflection on <c>OpenAIRequestPolicies._entries</c>).</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Returns the same <paramref name="agent"/> instance unchanged. The policy is installed
    /// on the chat client; the agent itself is not wrapped.
    /// </para>
    /// </remarks>
    internal static AIAgent TryApplyUserAgent(AIAgent agent)
    {
        var chatClient = agent.GetService<IChatClient>();
        if (chatClient?.GetService<OpenAIRequestPolicies>() is { } policies)
        {
            // Hosted agents are typically singletons resolved per request, so AddPolicy must be
            // called at most once per OpenAIRequestPolicies instance to avoid unbounded growth of
            // the policy list (each entry adds per-request CPU work even though the User-Agent
            // value stays stable). Track which instances we have already wired with a
            // ConditionalWeakTable keyed on the OpenAIRequestPolicies reference; the table holds
            // weak references so it does not extend the lifetime of the chat client.
            if (s_userAgentRegistrations.TryAdd(policies, s_boxedTrue))
            {
                policies.AddPolicy(HostedAgentUserAgentPolicy.Instance, PipelinePosition.PerCall);
            }
        }

        return agent;
    }

    private static readonly object s_boxedTrue = new();
    private static readonly ConditionalWeakTable<OpenAIRequestPolicies, object> s_userAgentRegistrations = new();
}
