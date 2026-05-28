// Copyright (c) Microsoft. All rights reserved.

using System;
using Microsoft.Agents.AI.Hosting.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Shared.Diagnostics;

#pragma warning disable IDE0130 // Namespace does not match folder structure — intentional: extension methods live in the host's framework namespace.
namespace Microsoft.AspNetCore.Builder;
#pragma warning restore IDE0130

/// <summary>
/// Extension methods on <see cref="IEndpointRouteBuilder"/> for mounting an agent-framework host.
/// </summary>
public static class EndpointRouteBuilderHostingChannelsExtensions
{
    /// <summary>
    /// Mounts every registered channel's routes (rooted at each channel's path) and invokes each channel's startup hook.
    /// </summary>
    public static IEndpointConventionBuilder MapAgentFrameworkHost(this IEndpointRouteBuilder endpoints)
    {
        Throw.IfNull(endpoints);

        var host = endpoints.ServiceProvider.GetRequiredService<AgentFrameworkHost>();
        // Force-construct the router so "hosting.push" is registered on the durable runner before traffic.
        _ = endpoints.ServiceProvider.GetRequiredService<ResponseRouter>();
        var context = new ChannelContext(endpoints.ServiceProvider, host);
        var hostGroup = endpoints.MapGroup(string.Empty);

        foreach (var channel in host.Channels)
        {
            var contribution = channel.Contribute(context);
            var channelGroup = string.IsNullOrEmpty(channel.Path)
                ? hostGroup
                : endpoints.MapGroup(channel.Path);

            foreach (var filter in contribution.EndpointFilters)
            {
                channelGroup.AddEndpointFilter(filter);
            }

            foreach (var register in contribution.Routes)
            {
                register(channelGroup);
            }
        }

        return hostGroup;
    }
}