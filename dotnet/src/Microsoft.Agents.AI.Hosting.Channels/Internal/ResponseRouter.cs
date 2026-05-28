// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Single owner of response-target resolution and durable push scheduling. Registered as a
/// singleton; binds the "hosting.push" handler on the configured <see cref="IDurableTaskRunner"/>
/// at construction.
/// </summary>
internal sealed class ResponseRouter
{
    /// <summary>Reserved handler name registered on the durable runner.</summary>
    public const string PushHandlerName = "hosting.push";

    private readonly AgentFrameworkHost _host;
    private readonly ILinkPolicy _linkPolicy;
    private readonly ILogger<ResponseRouter> _logger;
    private readonly Dictionary<string, Channel> _channelsByName;

    public ResponseRouter(
        AgentFrameworkHost host,
        ILinkPolicy linkPolicy,
        ILogger<ResponseRouter> logger)
    {
        this._host = Throw.IfNull(host);
        this._linkPolicy = Throw.IfNull(linkPolicy);
        this._logger = Throw.IfNull(logger);
        this._channelsByName = new Dictionary<string, Channel>(StringComparer.Ordinal);
        for (var i = 0; i < host.Channels.Count; i++)
        {
            this._channelsByName[host.Channels[i].Name] = host.Channels[i];
        }

        host.DurableRunner.Register(PushHandlerName, this.HandlePushAsync);
    }

    /// <summary>
    /// Resolve the destination set against the configured response target, schedule one
    /// <see cref="PushHandlerName"/> task per non-originating destination, and return the
    /// per-destination task handles.
    /// </summary>
    public async ValueTask<IReadOnlyList<TaskHandle>> ScheduleResponseAsync(
        HostedRunResult result,
        ChannelRequest originating,
        CancellationToken cancellationToken)
    {
        Throw.IfNull(result);
        Throw.IfNull(originating);

        var target = originating.ResponseTarget ?? ResponseTarget.Originating;
        if (target is ResponseTarget.OriginatingResponseTarget || target is ResponseTarget.NoneResponseTarget)
        {
            return Array.Empty<TaskHandle>();
        }

        var destinations = await this.ResolveDestinationsAsync(target, originating, cancellationToken).ConfigureAwait(false);
        if (destinations.Count == 0)
        {
            this._logger.LogWarning("Response target {Target} resolved to zero destinations for originating channel {Channel}. Falling back to originating.", target.GetType().Name, originating.Channel);
            return Array.Empty<TaskHandle>();
        }

        var handles = new List<TaskHandle>(destinations.Count);
        for (var i = 0; i < destinations.Count; i++)
        {
            var dest = destinations[i];
            var isOriginating = string.Equals(dest.Identity.Channel, originating.Channel, StringComparison.Ordinal);
            if (isOriginating)
            {
                continue;
            }

            if (!this._channelsByName.TryGetValue(dest.Identity.Channel, out var destChannel) || destChannel is not IChannelPush)
            {
                this._logger.LogWarning("Destination channel {Channel} is not registered or does not implement IChannelPush. Skipping.", dest.Identity.Channel);
                continue;
            }

            var pushContext = new ChannelPushContext
            {
                Destination = dest.Identity,
                OriginatingRequest = originating,
                OriginatingChannel = originating.Channel,
                IsEcho = false,
                OriginalTarget = target,
            };

            var payload = new HostingPushPayload(pushContext, result, dest.Identity.Channel, Originating: false);
            var handle = await this._host.DurableRunner.ScheduleAsync(PushHandlerName, payload, retryPolicy: null, cancellationToken).ConfigureAwait(false);
            handles.Add(handle);

            if (dest.EchoInput)
            {
                var echoContext = pushContext with { IsEcho = true };
                var echoPayload = new HostingPushPayload(echoContext, result, dest.Identity.Channel, Originating: false);
                handles.Add(await this._host.DurableRunner.ScheduleAsync(PushHandlerName, echoPayload, retryPolicy: null, cancellationToken).ConfigureAwait(false));
            }
        }

        return handles;
    }

    private async ValueTask<IReadOnlyList<ResolvedDestination>> ResolveDestinationsAsync(
        ResponseTarget target,
        ChannelRequest originating,
        CancellationToken cancellationToken)
    {
        var isolationKey = originating.Session?.IsolationKey;
        switch (target)
        {
            case ResponseTarget.ActiveResponseTarget:
                if (isolationKey is null) { return Array.Empty<ResolvedDestination>(); }
                var lastSeen = await this._host.StateStore.GetLastSeenAsync(isolationKey, cancellationToken).ConfigureAwait(false);
                return lastSeen is null
                    ? Array.Empty<ResolvedDestination>()
                    : await this.FilterByLinkPolicyAsync(originating.Channel, [new ResolvedDestination(lastSeen.Identity.Channel, lastSeen.Identity, EchoInput: false)], cancellationToken).ConfigureAwait(false);

            case ResponseTarget.AllLinkedResponseTarget:
                if (isolationKey is null) { return Array.Empty<ResolvedDestination>(); }
                var registrations = await this._host.StateStore.GetIdentitiesAsync(isolationKey, cancellationToken).ConfigureAwait(false);
                var all = new List<ResolvedDestination>(registrations.Count);
                for (var i = 0; i < registrations.Count; i++)
                {
                    all.Add(new ResolvedDestination(registrations[i].Identity.Channel, registrations[i].Identity, EchoInput: false));
                }
                return await this.FilterByLinkPolicyAsync(originating.Channel, all, cancellationToken).ConfigureAwait(false);

            case ResponseTarget.ChannelResponseTarget chTarget:
                return await this.ResolveChannelTargetsAsync(originating.Channel, isolationKey, [chTarget.ChannelName], chTarget.EchoInput, cancellationToken).ConfigureAwait(false);

            case ResponseTarget.ChannelsResponseTarget chsTarget:
                return await this.ResolveChannelTargetsAsync(originating.Channel, isolationKey, chsTarget.ChannelNames, chsTarget.EchoInput, cancellationToken).ConfigureAwait(false);

            case ResponseTarget.IdentitiesResponseTarget idTarget:
                var dests = new List<ResolvedDestination>(idTarget.Targets.Count);
                for (var i = 0; i < idTarget.Targets.Count; i++)
                {
                    dests.Add(new ResolvedDestination(idTarget.Targets[i].Channel, idTarget.Targets[i], idTarget.EchoInput));
                }
                return await this.FilterByLinkPolicyAsync(originating.Channel, dests, cancellationToken).ConfigureAwait(false);

            default:
                return Array.Empty<ResolvedDestination>();
        }
    }

    private async ValueTask<IReadOnlyList<ResolvedDestination>> ResolveChannelTargetsAsync(
        string originatingChannel,
        string? isolationKey,
        IReadOnlyList<string> channelNames,
        bool echoInput,
        CancellationToken cancellationToken)
    {
        if (isolationKey is null) { return Array.Empty<ResolvedDestination>(); }
        var registrations = await this._host.StateStore.GetIdentitiesAsync(isolationKey, cancellationToken).ConfigureAwait(false);

        var resolved = new List<ResolvedDestination>();
        var matchSet = new HashSet<string>(channelNames, StringComparer.Ordinal);
        for (var i = 0; i < registrations.Count; i++)
        {
            var reg = registrations[i];
            if (matchSet.Contains(reg.Identity.Channel))
            {
                resolved.Add(new ResolvedDestination(reg.Identity.Channel, reg.Identity, echoInput));
            }
        }
        return await this.FilterByLinkPolicyAsync(originatingChannel, resolved, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<ResolvedDestination>> FilterByLinkPolicyAsync(
        string originatingChannelName,
        List<ResolvedDestination> candidates,
        CancellationToken cancellationToken)
    {
        if (!this._channelsByName.TryGetValue(originatingChannelName, out var source))
        {
            return candidates;
        }

        var filtered = new List<ResolvedDestination>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            var dest = candidates[i];
            if (!this._channelsByName.TryGetValue(dest.ChannelName, out var destChannel))
            {
                continue;
            }
            var permitted = await this._linkPolicy.EvaluateAsync(
                new LinkPolicyContext { Source = source, Destination = destChannel, Operation = LinkPolicyOperation.Deliver },
                cancellationToken).ConfigureAwait(false);
            if (permitted) { filtered.Add(dest); }
        }
        return filtered;
    }

    private async ValueTask HandlePushAsync(TaskInvocationContext invocation)
    {
        if (invocation.Payload is not HostingPushPayload payload)
        {
            this._logger.LogError("hosting.push handler received unexpected payload type {Type}.", invocation.Payload?.GetType().FullName ?? "<null>");
            return;
        }

        if (!this._channelsByName.TryGetValue(payload.DestinationChannelName, out var channel))
        {
            this._logger.LogError("hosting.push handler could not resolve destination channel {Channel}.", payload.DestinationChannelName);
            return;
        }

        if (channel is not IChannelPush push)
        {
            this._logger.LogError("Destination channel {Channel} does not implement IChannelPush.", payload.DestinationChannelName);
            return;
        }

        // Echo idempotency cursor: never re-run a successful echo or response on retry.
        var stateKey = payload.PushContext.IsEcho ? "echo_done" : "response_done";
        if (invocation.State.TryGetValue(stateKey, out var done) && done is true)
        {
            return;
        }

        var resultForPush = payload.Result;
        if (channel is IChannelResponseHook hook)
        {
            var hookContext = new ChannelResponseContext
            {
                Request = payload.PushContext.OriginatingRequest,
                ChannelName = payload.DestinationChannelName,
                DestinationIdentity = payload.PushContext.Destination,
                Originating = payload.Originating,
                IsEcho = payload.PushContext.IsEcho,
            };
            resultForPush = await hook.OnResponseAsync(resultForPush, hookContext, CancellationToken.None).ConfigureAwait(false);
        }

        await push.PushAsync(payload.PushContext, resultForPush, CancellationToken.None).ConfigureAwait(false);
        invocation.State[stateKey] = true;
    }
}

internal sealed record ResolvedDestination(string ChannelName, ChannelIdentity Identity, bool EchoInput);