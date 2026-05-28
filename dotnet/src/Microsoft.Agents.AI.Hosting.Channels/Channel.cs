// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Base type for hosting channels. Authors derive from this to expose an inbound surface
/// (HTTP routes, long-poll loops, gateway connections, ...) and to optionally mix in capability
/// interfaces such as <see cref="IChannelPush"/>, <see cref="IChannelRunHook"/>, and
/// <see cref="IChannelResponseHook"/>.
/// </summary>
/// <remarks>
/// Two-phase lifecycle:
/// <list type="number">
/// <item><see cref="ConfigureServices"/> runs at <c>AddXxxChannel(...)</c> time, before DI is built.</item>
/// <item><see cref="Contribute"/> runs at <c>MapAgentFrameworkHost(...)</c> time, after DI is built.</item>
/// </list>
/// </remarks>
public abstract class Channel
{
    /// <summary>Stable channel name. Matches <see cref="ChannelRequest.Channel"/> / <see cref="ChannelIdentity.Channel"/>.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// Mount root for the channel's routes. The host wraps <see cref="ChannelContribution.Routes"/>
    /// in <c>endpoints.MapGroup(Path)</c> before invoking each action. Empty mounts at the host root.
    /// </summary>
    public virtual string Path => string.Empty;

    /// <summary>
    /// Whether this channel emits verified claims natively (e.g. an Activity Protocol bearer carrying
    /// an AAD <c>oid</c>). Read by the host's startup validator when sizing the link requirement.
    /// </summary>
    public virtual bool EmitsVerifiedClaims => false;

    /// <summary>
    /// Registers DI services the channel needs. Runs pre-<c>Build</c>.
    /// </summary>
    public virtual void ConfigureServices(IServiceCollection services)
    {
    }

    /// <summary>
    /// Returns the channel's contribution to the running host (routes, commands, startup / shutdown
    /// hooks, endpoint filters). Runs post-<c>Build</c>.
    /// </summary>
    public abstract ChannelContribution Contribute(IChannelContext context);
}