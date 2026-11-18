// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Records the one agent a Foundry hosting container serves, either as an agent instance or as a factory with its
/// lifetime, together with the protocol registration that named it.
/// </summary>
/// <remarks>
/// <para>
/// A container can map several protocols, for example Responses and A2A, and every protocol registration names the
/// agent it serves. They must all name the same agent, because Foundry publishes a dedicated endpoint for each hosted
/// agent. <see cref="ServiceCollectionExtensions.RegisterHostedAgent"/> adds the first registration and its
/// <see cref="FoundryHostedAgentSource"/>, and checks every later one against it.
/// </para>
/// <para>
/// Two registrations name the same agent when they pass the same agent instance, or the same factory delegate with
/// the same lifetime. Hosting cannot tell whether two different factories create the same agent, so they count as
/// different agents. To expose a factory-created agent over several protocols, pass one delegate to each of them.
/// </para>
/// </remarks>
internal sealed class FoundryHostedAgentRegistration
{
    private FoundryHostedAgentRegistration(
        AIAgent? agent,
        Func<IServiceProvider, AIAgent>? agentFactory,
        ServiceLifetime lifetime,
        string registeredBy)
    {
        this.Agent = agent;
        this.AgentFactory = agentFactory;
        this.Lifetime = lifetime;
        this.RegisteredBy = Throw.IfNullOrWhitespace(registeredBy);
    }

    /// <summary>Gets the agent instance the caller owns, or <see langword="null"/> for a factory registration.</summary>
    internal AIAgent? Agent { get; }

    /// <summary>Gets the factory that creates the agent, or <see langword="null"/> for an instance registration.</summary>
    internal Func<IServiceProvider, AIAgent>? AgentFactory { get; }

    /// <summary>Gets how long an agent created by <see cref="AgentFactory"/> is reused.</summary>
    internal ServiceLifetime Lifetime { get; }

    /// <summary>Gets the name of the protocol registration method that named the agent, used in error messages.</summary>
    internal string RegisteredBy { get; }

    /// <summary>
    /// Creates a registration for an agent instance the caller owns.
    /// </summary>
    /// <param name="agent">The agent that answers every request.</param>
    /// <param name="registeredBy">The name of the protocol registration method, for example <c>AddFoundryResponses</c>.</param>
    /// <returns>The registration.</returns>
    internal static FoundryHostedAgentRegistration ForInstance(AIAgent agent, string registeredBy) =>
        new(Throw.IfNull(agent), agentFactory: null, ServiceLifetime.Singleton, registeredBy);

    /// <summary>
    /// Creates a registration for an agent that <paramref name="agentFactory"/> creates with the specified lifetime.
    /// </summary>
    /// <param name="agentFactory">Creates the agent from the service provider of the root or of the current run.</param>
    /// <param name="lifetime">How long a created agent is reused.</param>
    /// <param name="registeredBy">The name of the protocol registration method, for example <c>AddFoundryResponses</c>.</param>
    /// <returns>The registration.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is not a defined lifetime.</exception>
    internal static FoundryHostedAgentRegistration ForFactory(
        Func<IServiceProvider, AIAgent> agentFactory,
        ServiceLifetime lifetime,
        string registeredBy)
    {
        _ = Throw.IfNull(agentFactory);
        FoundryHostedAgentSource.ValidateLifetime(lifetime);
        return new(agent: null, agentFactory, lifetime, registeredBy);
    }

    /// <summary>
    /// Determines whether <paramref name="other"/> names the same agent as this registration.
    /// </summary>
    /// <param name="other">The registration to compare with.</param>
    /// <returns><see langword="true"/> when both pass the same instance, or the same factory with the same lifetime.</returns>
    internal bool NamesSameAgentAs(FoundryHostedAgentRegistration other)
    {
        _ = Throw.IfNull(other);

        if (this.Agent is not null)
        {
            return ReferenceEquals(this.Agent, other.Agent);
        }

        // Delegate equality compares the target and the method, so passing one delegate variable, or the same method
        // group, to several protocol registrations matches, while two separately written lambdas do not.
        return other.AgentFactory is not null
            && this.AgentFactory!.Equals(other.AgentFactory)
            && this.Lifetime == other.Lifetime;
    }

    /// <summary>
    /// Describes the services that supply and own the agent: the <see cref="FoundryHostedAgentSource"/>, and for a
    /// singleton factory the owner that creates the agent once and disposes it at shutdown.
    /// </summary>
    /// <returns>The service descriptors to add.</returns>
    internal IReadOnlyList<ServiceDescriptor> DescribeServices()
    {
        if (this.Agent is { } agent)
        {
            // The instance registration is never disposed by the container, matching that the agent is not either.
            return [ServiceDescriptor.Singleton(new FoundryHostedAgentSource(agent))];
        }

        Func<IServiceProvider, AIAgent> agentFactory = this.AgentFactory!;
        ServiceLifetime lifetime = this.Lifetime;
        string registeredBy = this.RegisteredBy;

        // The source is created by the container, which disposes it, and with it a singleton agent, at shutdown.
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
                    ?? throw new InvalidOperationException($"The agent factory passed to {registeredBy} returned null.")));

        return [owner, source];
    }
}
