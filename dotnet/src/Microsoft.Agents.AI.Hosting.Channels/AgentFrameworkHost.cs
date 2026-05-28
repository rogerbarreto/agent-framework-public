// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// The central host instance composed by <c>AddAgentFrameworkHost(...)</c> and surfaced via DI.
/// Owns the authorization pipeline, the runner seam, the channels collection, and the bridges
/// to <see cref="IHostStateStore"/> + <see cref="IDurableTaskRunner"/>.
/// </summary>
/// <remarks>
/// This draft implements the run / stream / authorize entry points end-to-end against the in-memory
/// defaults. Background runs and response fan-out land in a follow-up commit alongside the channel
/// packages that consume them.
/// </remarks>
public sealed class AgentFrameworkHost
{
    private readonly IServiceProvider _services;

    internal AgentFrameworkHost(
        IServiceProvider services,
        IHostedTargetRunner targetRunner,
        IReadOnlyList<Channel> channels,
        IHostStateStore stateStore,
        IDurableTaskRunner durableRunner,
        AgentFrameworkHostOptions options)
    {
        this._services = Throw.IfNull(services);
        this.TargetRunner = Throw.IfNull(targetRunner);
        this.Channels = Throw.IfNull(channels);
        this.StateStore = Throw.IfNull(stateStore);
        this.DurableRunner = Throw.IfNull(durableRunner);
        this.Options = Throw.IfNull(options);
    }

    /// <summary>Application service provider.</summary>
    public IServiceProvider Services => this._services;

    /// <summary>Registered channels in registration order.</summary>
    public IReadOnlyList<Channel> Channels { get; }

    /// <summary>The configured target runner.</summary>
    public IHostedTargetRunner TargetRunner { get; }

    /// <summary>The shared host state store.</summary>
    public IHostStateStore StateStore { get; }

    /// <summary>The configured durable task runner.</summary>
    public IDurableTaskRunner DurableRunner { get; }

    /// <summary>Composition-time options.</summary>
    public AgentFrameworkHostOptions Options { get; }

    /// <summary>Run the target with the given request.</summary>
    public ValueTask<HostedRunResult> RunAsync(ChannelRequest request, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(request);
        return this.TargetRunner.RunAsync(request, cancellationToken);
    }

    /// <summary>Stream the target's response.</summary>
    public IAsyncEnumerable<HostedStreamItem> StreamAsync(ChannelRequest request, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(request);
        return this.TargetRunner.StreamAsync(request, cancellationToken);
    }

    /// <summary>
    /// Schedule a request to run in the background. Returns a <see cref="ContinuationToken"/> the
    /// caller can poll for completion.
    /// </summary>
    public async ValueTask<ContinuationToken> RunInBackgroundAsync(
        ChannelRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(request);

        var token = new ContinuationToken
        {
            Token = Guid.NewGuid().ToString("N"),
            Status = ContinuationStatus.Queued,
            IsolationKey = request.Session?.IsolationKey,
            CreatedAt = DateTimeOffset.UtcNow,
            ResponseTarget = request.ResponseTarget,
        };

        await this.StateStore.SaveContinuationAsync(token, cancellationToken).ConfigureAwait(false);
        return token;
    }

    /// <summary>Retrieve a previously-scheduled continuation token by its opaque id.</summary>
    public ValueTask<ContinuationToken?> GetContinuationAsync(string token, CancellationToken cancellationToken = default) =>
        this.StateStore.GetContinuationAsync(token, cancellationToken);

    /// <summary>Rotate the active session alias for an isolation key (host-tracked channels' <c>/new</c>).</summary>
    public ValueTask ResetSessionAsync(string isolationKey, CancellationToken cancellationToken = default) =>
        this.StateStore.RotateSessionAliasAsync(isolationKey, cancellationToken);

    /// <summary>Funnel an identity through the host's authorization pipeline.</summary>
    public async ValueTask<AuthorizationOutcome> AuthorizeAsync(
        ChannelIdentity identity,
        AuthorizationRequest options,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(identity);
        Throw.IfNull(options);

        var allowlist = options.Allowlist ?? this.Options.DefaultAllowlist ?? AllowAllIdentityAllowlist.Instance;
        var linker = this._services.GetService<IIdentityLinker>();
        var verifiedClaims = options.VerifiedClaims;
        var claimSource = verifiedClaims is null or { Count: 0 } ? ClaimSource.None : ClaimSource.Channel;

        var preContext = new AuthorizationContext
        {
            Identity = identity,
            Phase = AuthorizationPhase.PreLink,
            VerifiedClaims = verifiedClaims ?? new Dictionary<string, string>(),
            ClaimSource = claimSource,
            ConversationContext = options.ConversationContext,
        };

        var preDecision = await allowlist.EvaluateAsync(preContext, cancellationToken).ConfigureAwait(false);
        switch (preDecision)
        {
            case AllowlistDecision.Deny:
                return new AuthorizationOutcome.Denied("allowlist_denied_pre_link");

            case AllowlistDecision.Allow:
                if (options.RequireLink && linker is not null)
                {
                    var linkedKey = await linker.IsLinkedAsync(identity, verifiedClaims, cancellationToken).ConfigureAwait(false);
                    if (linkedKey is not null)
                    {
                        await this.StateStore.SaveLinkAsync(identity, linkedKey, verifiedClaims, cancellationToken).ConfigureAwait(false);
                        return new AuthorizationOutcome.Allowed(linkedKey);
                    }
                    var challenge = await linker.BeginAsync(identity, requestedIsolationKey: null, cancellationToken).ConfigureAwait(false);
                    return new AuthorizationOutcome.LinkRequired(challenge);
                }
                {
                    var key = await this.ResolveOrIssueIsolationKeyAsync(identity, cancellationToken).ConfigureAwait(false);
                    return new AuthorizationOutcome.Allowed(key);
                }

            case AllowlistDecision.Abstain:
                if ((options.RequireLink || allowlist.RequiresLinkedClaims) && linker is not null)
                {
                    var linkedKey = await linker.IsLinkedAsync(identity, verifiedClaims, cancellationToken).ConfigureAwait(false);
                    if (linkedKey is null)
                    {
                        var challenge = await linker.BeginAsync(identity, requestedIsolationKey: null, cancellationToken).ConfigureAwait(false);
                        return new AuthorizationOutcome.LinkRequired(challenge);
                    }

                    var linked = await this.StateStore.GetIdentitiesAsync(linkedKey, cancellationToken).ConfigureAwait(false);
                    Dictionary<string, string> mergedClaims = new();
                    foreach (var reg in linked)
                    {
                        foreach (var (k, v) in reg.VerifiedClaims) { mergedClaims[k] = v; }
                    }
                    if (verifiedClaims is not null)
                    {
                        foreach (var (k, v) in verifiedClaims) { mergedClaims[k] = v; }
                    }

                    var postContext = preContext with
                    {
                        Phase = AuthorizationPhase.PostLink,
                        IsolationKey = linkedKey,
                        VerifiedClaims = mergedClaims,
                        ClaimSource = ClaimSource.Linker,
                    };
                    var postDecision = await allowlist.EvaluateAsync(postContext, cancellationToken).ConfigureAwait(false);
                    return postDecision switch
                    {
                        AllowlistDecision.Allow => new AuthorizationOutcome.Allowed(linkedKey),
                        AllowlistDecision.Deny => new AuthorizationOutcome.Denied("allowlist_denied_post_link"),
                        _ => new AuthorizationOutcome.Denied("allowlist_abstain_after_link"),
                    };
                }
                {
                    var key = await this.ResolveOrIssueIsolationKeyAsync(identity, cancellationToken).ConfigureAwait(false);
                    return new AuthorizationOutcome.Allowed(key);
                }
        }

        // Unreachable: AllowlistDecision is exhaustive.
        throw new InvalidOperationException("Unhandled allowlist decision.");
    }

    private async ValueTask<string> ResolveOrIssueIsolationKeyAsync(ChannelIdentity identity, CancellationToken cancellationToken)
    {
        var existing = await this.StateStore.GetIsolationKeyAsync(identity, cancellationToken).ConfigureAwait(false);
        if (existing is not null) { return existing; }

        var issued = $"{identity.Channel}:{identity.NativeId}";
        await this.StateStore.SaveLinkAsync(identity, issued, verifiedClaims: null, cancellationToken).ConfigureAwait(false);
        return issued;
    }
}