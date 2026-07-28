// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Azure.AI.AgentServer.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace Microsoft.Agents.AI.Foundry.Hosting.UnitTests;

/// <summary>
/// Verifies that <c>AddFoundryResponses</c> binds Kestrel to the Foundry hosted-runtime port
/// (<see cref="FoundryEnvironment.Port"/>, the <c>PORT</c> env var, default 8088) for a plain
/// <c>WebApplication.CreateBuilder</c> (Tier 3) host, so a source (ZIP) deployed agent passes the
/// platform readiness probe with no Dockerfile pinning the port.
/// </summary>
[Collection(AspNetCoreUrlsEnvFixture.Name)]
public sealed class FoundryListenPortTests
{
    private const string AspNetCoreUrls = "ASPNETCORE_URLS";

    [Fact]
    public void AddFoundryResponses_BindsFoundryPort()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddFoundryResponses();

        // Assert
        Assert.Contains(FoundryEnvironment.Port, GetCodeBackedPorts(services));
    }

    [Fact]
    public void AddFoundryResponses_WithAgent_BindsFoundryPort()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var mockAgent = new Mock<AIAgent>();
        mockAgent.SetupGet(a => a.Name).Returns("test-agent");

        // Act
        services.AddFoundryResponses(mockAgent.Object);

        // Assert
        Assert.Contains(FoundryEnvironment.Port, GetCodeBackedPorts(services));
    }

    [Fact]
    public void AddFoundryResponses_WithAspNetCoreUrlsSet_StillBindsFoundryPort()
    {
        // Arrange: the .NET base image used by source (ZIP) deploy sets ASPNETCORE_URLS to port 80.
        // The binding must still be applied, because a listener configured in code takes precedence
        // over that setting. Skipping it here would leave the container on port 80 and fail every
        // invocation with HTTP 424 session_not_ready.
        var original = Environment.GetEnvironmentVariable(AspNetCoreUrls);
        try
        {
            Environment.SetEnvironmentVariable(AspNetCoreUrls, "http://+:80");
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddFoundryResponses();

            // Assert
            Assert.Contains(FoundryEnvironment.Port, GetCodeBackedPorts(services));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AspNetCoreUrls, original);
        }
    }

    [Fact]
    public void AddFoundryResponses_CalledTwice_BindsFoundryPortOnce()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddFoundryResponses();
        services.AddFoundryResponses();

        // Assert: a duplicate ListenAnyIP on the same port fails Kestrel startup with
        // "address already in use", so the binding must be applied exactly once.
        Assert.Equal(1, GetCodeBackedPorts(services).Count(port => port == FoundryEnvironment.Port));
    }

    /// <summary>
    /// Builds the service provider, resolves the applied <see cref="KestrelServerOptions"/>, and
    /// returns the ports of every code-configured listener (those added via <c>ListenAnyIP</c>).
    /// </summary>
    private static List<int> GetCodeBackedPorts(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        var property = typeof(KestrelServerOptions).GetProperty(
            "CodeBackedListenOptions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);

        var listenOptions = (IEnumerable)property!.GetValue(options)!;
        var ports = new List<int>();
        foreach (var listenOption in listenOptions)
        {
            if (listenOption.GetType().GetProperty("IPEndPoint")?.GetValue(listenOption) is IPEndPoint endpoint)
            {
                ports.Add(endpoint.Port);
            }
        }

        return ports;
    }
}
