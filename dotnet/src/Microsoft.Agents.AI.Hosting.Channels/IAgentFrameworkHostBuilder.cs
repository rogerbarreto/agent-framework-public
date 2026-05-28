// Copyright (c) Microsoft. All rights reserved.

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Builder chained off <c>AddAgentFrameworkHost(...)</c>. Channel-add extension methods
/// (<c>AddResponsesChannel</c>, <c>AddInvocationsChannel</c>, <c>AddTelegramChannel</c>, ...)
/// hang off this interface.
/// </summary>
public interface IAgentFrameworkHostBuilder
{
    /// <summary>Underlying service collection.</summary>
    IServiceCollection Services { get; }

    /// <summary>Composition-time options.</summary>
    AgentFrameworkHostOptions Options { get; }

    /// <summary>Add a channel instance.</summary>
    IAgentFrameworkHostBuilder AddChannel(Channel channel);

    /// <summary>Add a channel resolved from DI via a factory.</summary>
    IAgentFrameworkHostBuilder AddChannel<TChannel>(Func<IServiceProvider, TChannel> factory) where TChannel : Channel;

    /// <summary>Replace the registered <see cref="IIdentityLinker"/>.</summary>
    IAgentFrameworkHostBuilder UseIdentityLinker<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TLinker>()
        where TLinker : class, IIdentityLinker;

    /// <summary>Replace the host-level default allowlist.</summary>
    IAgentFrameworkHostBuilder UseDefaultAllowlist(IIdentityAllowlist allowlist);

    /// <summary>Replace the registered link policy.</summary>
    IAgentFrameworkHostBuilder UseLinkPolicy(ILinkPolicy policy);

    /// <summary>Replace the registered durable task runner.</summary>
    IAgentFrameworkHostBuilder UseDurableTaskRunner<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TRunner>()
        where TRunner : class, IDurableTaskRunner;

    /// <summary>Replace the registered host state store.</summary>
    IAgentFrameworkHostBuilder UseHostStateStore<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>()
        where TStore : class, IHostStateStore;
}