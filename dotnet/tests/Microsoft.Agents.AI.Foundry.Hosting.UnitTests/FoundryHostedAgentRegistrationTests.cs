// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Microsoft.Agents.AI.Foundry.Hosting.UnitTests;

public class FoundryHostedAgentRegistrationTests
{
    [Fact]
    public void RegisterHostedAgent_SameInstanceFromAnotherProtocol_ReusesTheFirstRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        AIAgent agent = Mock.Of<AIAgent>();
        ServiceCollectionExtensions.RegisterHostedAgent(services, FoundryHostedAgentRegistration.ForInstance(agent, "ProtocolA"));
        int registrations = services.Count;

        // Act
        ServiceCollectionExtensions.RegisterHostedAgent(services, FoundryHostedAgentRegistration.ForInstance(agent, "ProtocolB"));

        // Assert
        Assert.Equal(registrations, services.Count);
        Assert.Single(services, d => d.ServiceType == typeof(FoundryHostedAgentSource));
        Assert.Equal("ProtocolA", GetRegistration(services).RegisteredBy);
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void RegisterHostedAgent_SameFactoryAndLifetimeFromAnotherProtocol_ReusesTheFirstRegistration(ServiceLifetime lifetime)
    {
        // Arrange
        var services = new ServiceCollection();
        Func<IServiceProvider, AIAgent> factory = CreateAgent;
        ServiceCollectionExtensions.RegisterHostedAgent(services, FoundryHostedAgentRegistration.ForFactory(factory, lifetime, "ProtocolA"));
        int registrations = services.Count;

        // Act
        ServiceCollectionExtensions.RegisterHostedAgent(services, FoundryHostedAgentRegistration.ForFactory(factory, lifetime, "ProtocolB"));

        // Assert
        Assert.Equal(registrations, services.Count);
        Assert.Single(services, d => d.ServiceType == typeof(FoundryHostedAgentSource));
    }

    [Fact]
    public void RegisterHostedAgent_SameMethodGroupFromAnotherProtocol_ReusesTheFirstRegistration()
    {
        // Arrange: a method group creates a new delegate instance for each call, but it names the same agent factory.
        var services = new ServiceCollection();
        ServiceCollectionExtensions.RegisterHostedAgent(
            services,
            FoundryHostedAgentRegistration.ForFactory(CreateAgent, ServiceLifetime.Scoped, "ProtocolA"));
        int registrations = services.Count;

        // Act
        ServiceCollectionExtensions.RegisterHostedAgent(
            services,
            FoundryHostedAgentRegistration.ForFactory(CreateAgent, ServiceLifetime.Scoped, "ProtocolB"));

        // Assert
        Assert.Equal(registrations, services.Count);
    }

    [Fact]
    public void RegisterHostedAgent_DifferentInstance_ThrowsAndKeepsTheFirstRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        ServiceCollectionExtensions.RegisterHostedAgent(services, FoundryHostedAgentRegistration.ForInstance(Mock.Of<AIAgent>(), "ProtocolA"));
        int registrations = services.Count;

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => ServiceCollectionExtensions.RegisterHostedAgent(
            services,
            FoundryHostedAgentRegistration.ForInstance(Mock.Of<AIAgent>(), "ProtocolB")));

        // Assert
        Assert.Contains("exactly one agent", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ProtocolA", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ProtocolB", exception.Message, StringComparison.Ordinal);
        Assert.Equal(registrations, services.Count);
    }

    [Fact]
    public void RegisterHostedAgent_DifferentFactory_Throws()
    {
        // Arrange: two separately written lambdas cannot be shown to create the same agent.
        var services = new ServiceCollection();
        ServiceCollectionExtensions.RegisterHostedAgent(
            services,
            FoundryHostedAgentRegistration.ForFactory(_ => Mock.Of<AIAgent>(), ServiceLifetime.Scoped, "ProtocolA"));

        // Act and assert
        Assert.Throws<InvalidOperationException>(() => ServiceCollectionExtensions.RegisterHostedAgent(
            services,
            FoundryHostedAgentRegistration.ForFactory(_ => Mock.Of<AIAgent>(), ServiceLifetime.Scoped, "ProtocolB")));
    }

    [Fact]
    public void RegisterHostedAgent_SameFactoryWithDifferentLifetime_Throws()
    {
        // Arrange
        var services = new ServiceCollection();
        Func<IServiceProvider, AIAgent> factory = CreateAgent;
        ServiceCollectionExtensions.RegisterHostedAgent(services, FoundryHostedAgentRegistration.ForFactory(factory, ServiceLifetime.Singleton, "ProtocolA"));

        // Act and assert
        Assert.Throws<InvalidOperationException>(() => ServiceCollectionExtensions.RegisterHostedAgent(
            services,
            FoundryHostedAgentRegistration.ForFactory(factory, ServiceLifetime.Scoped, "ProtocolB")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RegisterHostedAgent_InstanceAndFactory_Throws(bool instanceFirst)
    {
        // Arrange
        var services = new ServiceCollection();
        AIAgent agent = Mock.Of<AIAgent>();
        FoundryHostedAgentRegistration instance = FoundryHostedAgentRegistration.ForInstance(agent, "ProtocolA");
        FoundryHostedAgentRegistration factory = FoundryHostedAgentRegistration.ForFactory(_ => agent, ServiceLifetime.Singleton, "ProtocolB");
        ServiceCollectionExtensions.RegisterHostedAgent(services, instanceFirst ? instance : factory);

        // Act and assert
        Assert.Throws<InvalidOperationException>(
            () => ServiceCollectionExtensions.RegisterHostedAgent(services, instanceFirst ? factory : instance));
    }

    private static AIAgent CreateAgent(IServiceProvider _) => Mock.Of<AIAgent>();

    private static FoundryHostedAgentRegistration GetRegistration(ServiceCollection services) =>
        services
            .Where(d => d.ServiceType == typeof(FoundryHostedAgentRegistration))
            .Select(d => d.ImplementationInstance)
            .OfType<FoundryHostedAgentRegistration>()
            .Single();
}
