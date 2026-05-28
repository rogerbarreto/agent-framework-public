// Copyright (c) Microsoft. All rights reserved.

using System;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels.Invocations;

/// <summary>
/// Extensions on <see cref="IAgentFrameworkHostBuilder"/> for the invocations channel.
/// </summary>
public static class AgentFrameworkHostBuilderInvocationsExtensions
{
    /// <summary>Add the JSON invocations channel.</summary>
    public static IAgentFrameworkHostBuilder AddInvocationsChannel(
        this IAgentFrameworkHostBuilder builder,
        Action<InvocationsChannelOptions>? configure = null)
    {
        Throw.IfNull(builder);
        var options = new InvocationsChannelOptions();
        configure?.Invoke(options);
        return builder.AddChannel(new InvocationsChannel(options));
    }
}