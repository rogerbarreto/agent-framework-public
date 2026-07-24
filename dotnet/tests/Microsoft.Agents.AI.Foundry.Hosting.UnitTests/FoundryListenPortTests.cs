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
/// <c>WebApplication.CreateBuilder</c> (Tier 3) host, so the sample works with no Dockerfile and
/// no <c>ASPNETCORE_URLS</c>, while still respecting an explicit <c>ASPNETCORE_URLS</c> override.
/// </summary>
[Collection(AspNetCoreUrlsEnvFixture.Name)]
public sealed class FoundryListenPortTests
{
    private const string AspNetCoreUrls = "ASPNETCORE_URLS";

    [Fact]
    public void AddFoundryResponses_NoAspNetCoreUrls_BindsFoundryPort()
    {
        // Arrange
        var original = Environment.GetEnvironmentVariable(AspNetCoreUrls);
        try
        {
            Environment.SetEnvironmentVariable(AspNetCoreUrls, null);
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddFoundryResponses();

            // Assert
            var ports = GetCodeBackedPorts(services);
            Assert.Contains(FoundryEnvironment.Port, ports);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AspNetCoreUrls, original);
        }
    }

    [Fact]
    public void AddFoundryResponses_WithAgent_NoAspNetCoreUrls_BindsFoundryPort()
    {
        // Arrange
        var original = Environment.GetEnvironmentVariable(AspNetCoreUrls);
        try
        {
            Environment.SetEnvironmentVariable(AspNetCoreUrls, null);
            var services = new ServiceCollection();
            services.AddLogging();
            var mockAgent = new Mock<AIAgent>();
            mockAgent.SetupGet(a => a.Name).Returns("test-agent");

            // Act
            services.AddFoundryResponses(mockAgent.Object);

            // Assert
            var ports = GetCodeBackedPorts(services);
            Assert.Contains(FoundryEnvironment.Port, ports);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AspNetCoreUrls, original);
        }
    }

    [Fact]
    public void AddFoundryResponses_WithAspNetCoreUrls_DoesNotBindFoundryPort()
    {
        // Arrange: an explicit ASP.NET Core URL binding must win; the package adds no listener.
        var original = Environment.GetEnvironmentVariable(AspNetCoreUrls);
        try
        {
            Environment.SetEnvironmentVariable(AspNetCoreUrls, "http://+:9123");
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddFoundryResponses();

            // Assert
            var ports = GetCodeBackedPorts(services);
            Assert.DoesNotContain(FoundryEnvironment.Port, ports);
            Assert.DoesNotContain(9123, ports);
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
        var original = Environment.GetEnvironmentVariable(AspNetCoreUrls);
        try
        {
            Environment.SetEnvironmentVariable(AspNetCoreUrls, null);
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddFoundryResponses();
            services.AddFoundryResponses();

            // Assert: the listen port is configured exactly once, not duplicated (a duplicate
            // ListenAnyIP on the same port would fail Kestrel startup with "address already in use").
            var ports = GetCodeBackedPorts(services);
            Assert.Equal(1, ports.Count(p => p == FoundryEnvironment.Port));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AspNetCoreUrls, original);
        }
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
