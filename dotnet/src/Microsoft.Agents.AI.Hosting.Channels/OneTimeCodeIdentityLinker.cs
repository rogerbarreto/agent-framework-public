// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Zero-dependency <see cref="IIdentityLinker"/>: <see cref="BeginAsync"/> emits a short random code
/// the user must present on a peer channel; <see cref="CompleteAsync"/> consumes the code and binds
/// the peer-channel identity to the originating identity's isolation key. Backed entirely by
/// <see cref="IHostStateStore"/>; no callback routes are required so <see cref="Contribute"/> is a no-op.
/// </summary>
/// <remarks>
/// Use this for low-ceremony cross-channel linking (Telegram + Responses, Telegram + Discord, ...)
/// where one channel asks the user to type a code into the other. For Entra / OAuth-style flows
/// substitute the Entra linker from <c>Microsoft.Agents.AI.Hosting.Channels.EntraId</c>.
/// </remarks>
public sealed class OneTimeCodeIdentityLinker : IIdentityLinker
{
    private readonly IHostStateStore _stateStore;

    /// <summary>Initializes a new instance.</summary>
    public OneTimeCodeIdentityLinker(IHostStateStore stateStore)
    {
        this._stateStore = Throw.IfNull(stateStore);
    }

    /// <inheritdoc />
    public string Name => "one-time-code";

    /// <summary>Lifetime of an unconsumed code. Default 10 minutes.</summary>
    public TimeSpan CodeLifetime { get; init; } = TimeSpan.FromMinutes(10);

    /// <inheritdoc />
    public ChannelContribution Contribute(IChannelContext context) => new();

    /// <inheritdoc />
    public async ValueTask<LinkChallenge> BeginAsync(
        ChannelIdentity identity,
        string? requestedIsolationKey,
        CancellationToken cancellationToken)
    {
        Throw.IfNull(identity);

        var code = GenerateCode();
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["channel"] = identity.Channel,
            ["nativeId"] = identity.NativeId,
        };

        var grant = new LinkGrant(
            Code: code,
            IssuedByLinker: this.Name,
            RequestedIsolationKey: requestedIsolationKey,
            ExpiresAt: DateTimeOffset.UtcNow.Add(this.CodeLifetime),
            Payload: payload);

        await this._stateStore.SaveLinkGrantAsync(grant, cancellationToken).ConfigureAwait(false);

        return new LinkChallenge(
            ChallengeId: code,
            Kind: "code",
            Code: code,
            UserPrompt: $"Send '/link {code}' on the other channel to merge the two identities. Code expires in {(int)this.CodeLifetime.TotalMinutes} minutes.");
    }

    /// <inheritdoc />
    public async ValueTask<PrincipalIdentity> CompleteAsync(
        string challengeId,
        IReadOnlyDictionary<string, object?> proof,
        CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(challengeId);
        Throw.IfNull(proof);

        if (!proof.TryGetValue("identity", out var identityObj) || identityObj is not ChannelIdentity completingIdentity)
        {
            throw new ArgumentException("Proof must include 'identity' of type ChannelIdentity.", nameof(proof));
        }

        var grant = await this._stateStore.ConsumeLinkGrantAsync(challengeId, cancellationToken).ConfigureAwait(false);
        if (grant is null)
        {
            throw new InvalidOperationException($"Link code '{challengeId}' is invalid, expired, or already consumed.");
        }

        if (!grant.Payload.TryGetValue("channel", out var sourceChannel) ||
            !grant.Payload.TryGetValue("nativeId", out var sourceNativeId) ||
            sourceChannel is not string sourceChannelStr ||
            sourceNativeId is not string sourceNativeIdStr)
        {
            throw new InvalidOperationException($"Link code '{challengeId}' has a malformed payload.");
        }

        var sourceIdentity = new ChannelIdentity(sourceChannelStr, sourceNativeIdStr);

        var isolationKey = grant.RequestedIsolationKey
            ?? await this._stateStore.GetIsolationKeyAsync(sourceIdentity, cancellationToken).ConfigureAwait(false)
            ?? await this._stateStore.GetIsolationKeyAsync(completingIdentity, cancellationToken).ConfigureAwait(false)
            ?? $"{sourceIdentity.Channel}:{sourceIdentity.NativeId}";

        await this._stateStore.SaveLinkAsync(sourceIdentity, isolationKey, verifiedClaims: null, cancellationToken).ConfigureAwait(false);
        await this._stateStore.SaveLinkAsync(completingIdentity, isolationKey, verifiedClaims: null, cancellationToken).ConfigureAwait(false);

        return new PrincipalIdentity(isolationKey, completingIdentity, new Dictionary<string, string>(StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public async ValueTask<string?> IsLinkedAsync(
        ChannelIdentity identity,
        IReadOnlyDictionary<string, string>? verifiedClaims,
        CancellationToken cancellationToken)
    {
        Throw.IfNull(identity);
        var existing = await this._stateStore.GetIsolationKeyAsync(identity, cancellationToken).ConfigureAwait(false);
        if (existing is not null) { return existing; }

        if (verifiedClaims is not null)
        {
            foreach (var (claim, value) in verifiedClaims)
            {
                var match = await this._stateStore.LookupByVerifiedClaimAsync(claim, value, cancellationToken).ConfigureAwait(false);
                if (match is not null)
                {
                    await this._stateStore.SaveLinkAsync(identity, match, verifiedClaims, cancellationToken).ConfigureAwait(false);
                    return match;
                }
            }
        }

        return null;
    }

    private static string GenerateCode()
    {
        Span<byte> bytes = stackalloc byte[5];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder(8);
        const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (var i = 0; i < bytes.Length; i++)
        {
            sb.Append(Alphabet[bytes[i] % Alphabet.Length]);
        }
        return sb.ToString();
    }
}