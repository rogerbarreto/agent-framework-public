// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Agents.AI.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq;

namespace Microsoft.Agents.AI.Foundry.Hosting.UnitTests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFoundryToolboxService_MarksFeatureUsed()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddFoundryToolboxService(Mock.Of<TokenCredential>(), "toolbox");

        // Assert
        FeatureUsageAssert.Marked(52);
    }

    [Fact]
    public void AddFoundryToolboxService_DoesNotChangeHealthChecks()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHealthChecks().AddCheck("custom", () => HealthCheckResult.Healthy());

        // Act
        services.AddFoundryToolboxService(Mock.Of<TokenCredential>(), "toolbox");
        using ServiceProvider provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        // Assert
        Assert.Equal("custom", Assert.Single(options.Registrations).Name);
    }

    [Fact]
    public async Task AddFoundryToolboxService_OptionsObject_CopiesOptionalBehaviorAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        var suppliedOptions = new FoundryToolboxServiceOptions
        {
            TokenScope = "https://example.test/.default",
            AllowedOAuthConsentOrigins =
                new HashSet<Uri> { new("https://login.example.test") },
        };

        // Act
        services.AddFoundryToolboxService(
            Mock.Of<TokenCredential>(),
            suppliedOptions,
            "first-toolbox",
            " ",
            "second-toolbox");
        suppliedOptions.AllowedOAuthConsentOrigins.Clear();
        suppliedOptions.AllowedOAuthConsentOrigins.Add(new Uri("https://mutated.example.test"));
        await using ServiceProvider provider = services.BuildServiceProvider();
        FoundryToolboxService service = provider.GetRequiredService<FoundryToolboxService>();
        FieldInfo optionsField = typeof(FoundryToolboxService).GetField(
            "_options",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Foundry toolbox options field not found.");
        FoundryToolboxServiceOptions registeredOptions =
            Assert.IsType<FoundryToolboxServiceOptions>(optionsField.GetValue(service));

        // Assert
        Assert.Equal("https://example.test/.default", registeredOptions.TokenScope);
        ISet<Uri> allowedOrigins = Assert.IsAssignableFrom<ISet<Uri>>(registeredOptions.AllowedOAuthConsentOrigins);
        Assert.Contains(new Uri("https://login.example.test"), allowedOrigins);
        Assert.DoesNotContain(new Uri("https://mutated.example.test"), allowedOrigins);
        Assert.Equal(["first-toolbox", "second-toolbox"], registeredOptions.ToolboxNames);
    }
}
