// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace Microsoft.Agents.AI.Foundry.Hosting.UnitTests;

/// <summary>
/// Verifies that <c>AddFoundryResponses</c> binds Kestrel to the Foundry hosted-runtime port
/// (<see cref="FoundryEnvironment.Port"/>, the <c>PORT</c> env var, default 8088) for a plain
/// <c>WebApplication.CreateBuilder</c> (Tier 3) host, so a source (ZIP) deployed agent passes the
/// platform readiness probe with no Dockerfile pinning the port, and that it leaves the addresses
/// of a host running outside Foundry alone.
/// </summary>
[Collection(HostingEnvFixture.Name)]
public sealed class FoundryListenPortTests
{
    private const string AspNetCoreUrls = "ASPNETCORE_URLS";

    [Fact]
    public void AddFoundryResponses_WhenHosted_BindsFoundryPort()
    {
        // Arrange
        using var hosted = new FoundryHostingScope();
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddFoundryResponses();

        // Assert
        Assert.Contains(FoundryEnvironment.Port, GetCodeBackedPorts(services));
    }

    [Fact]
    public void AddFoundryResponses_WithAgentWhenHosted_BindsFoundryPort()
    {
        // Arrange
        using var hosted = new FoundryHostingScope();
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
    public void AddFoundryResponses_WhenNotHosted_LeavesAddressesAlone()
    {
        // Arrange: outside a Foundry container the host keeps whatever addresses it resolved from
        // configuration, so registering the Responses protocol must not add a listener.
        using var notHosted = new FoundryHostingScope(hosted: false);
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddFoundryResponses();

        // Assert
        Assert.Empty(GetCodeBackedPorts(services));
    }

    [Fact]
    public void AddFoundryResponses_WhenHostedWithAspNetCoreUrlsSet_StillBindsFoundryPort()
    {
        // Arrange: the .NET base image used by source (ZIP) deploy sets ASPNETCORE_URLS to port 80.
        // Inside Foundry the binding must still be applied, because a listener configured in code
        // takes precedence over that setting. Skipping it here would leave the container on port 80
        // and fail every invocation with HTTP 424 session_not_ready.
        using var hosted = new FoundryHostingScope();
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
    public void AddFoundryResponses_CalledTwiceWhenHosted_BindsFoundryPortOnce()
    {
        // Arrange
        using var hosted = new FoundryHostingScope();
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

    /// <summary>
    /// Sets the variable the Foundry platform injects into a hosted container for the duration of a
    /// test, and restores the previous value on dispose.
    /// </summary>
    private sealed class FoundryHostingScope : IDisposable
    {
        private readonly string? _original;

        public FoundryHostingScope(bool hosted = true)
        {
            this._original = Environment.GetEnvironmentVariable(FoundryHostingExtensions.FoundryHostingEnvironmentVariable);
            Environment.SetEnvironmentVariable(
                FoundryHostingExtensions.FoundryHostingEnvironmentVariable,
                hosted ? "foundry" : null);
        }

        public void Dispose() =>
            Environment.SetEnvironmentVariable(FoundryHostingExtensions.FoundryHostingEnvironmentVariable, this._original);
    }
}
