// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// In-memory <see cref="IHostStateStore"/>. Volatile; intended for tests, samples, and single-process
/// development scenarios. Thread-safe.
/// </summary>
public sealed class InMemoryHostStateStore : IHostStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Channel, string NativeId), string> _identityToKey = new();
    private readonly Dictionary<string, List<ChannelIdentityRegistration>> _keyToIdentities = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Claim, string Value), string> _claimToKey = new();
    private readonly ConcurrentDictionary<string, LinkGrant> _linkGrants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LastSeenRecord> _lastSeen = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ContinuationToken> _continuations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _sessionAliases = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<string?> GetIsolationKeyAsync(ChannelIdentity identity, CancellationToken cancellationToken)
    {
        Throw.IfNull(identity);
        lock (this._gate)
        {
            return new(this._identityToKey.TryGetValue((identity.Channel, identity.NativeId), out var key) ? key : null);
        }
    }

    /// <inheritdoc />
    public ValueTask SaveLinkAsync(
        ChannelIdentity identity,
        string isolationKey,
        IReadOnlyDictionary<string, string>? verifiedClaims,
        CancellationToken cancellationToken)
    {
        Throw.IfNull(identity);
        Throw.IfNullOrEmpty(isolationKey);

        lock (this._gate)
        {
            var key = (identity.Channel, identity.NativeId);
            if (this._identityToKey.TryGetValue(key, out var existing) && existing != isolationKey)
            {
                // Merge: move all identities under `existing` into `isolationKey`.
                if (this._keyToIdentities.TryGetValue(existing, out var existingList))
                {
                    foreach (var reg in existingList)
                    {
                        this._identityToKey[(reg.Identity.Channel, reg.Identity.NativeId)] = isolationKey;
                        this.GetOrCreateList(isolationKey).Add(reg);
                    }
                    this._keyToIdentities.Remove(existing);
                }
            }

            this._identityToKey[key] = isolationKey;
            var claims = verifiedClaims ?? ImmutableDictionary<string, string>.Empty;
            var registration = new ChannelIdentityRegistration(identity, DateTimeOffset.UtcNow, claims);

            var list = this.GetOrCreateList(isolationKey);
            list.RemoveAll(r => r.Identity.Channel == identity.Channel && r.Identity.NativeId == identity.NativeId);
            list.Add(registration);

            foreach (var (claim, value) in claims)
            {
                this._claimToKey[(claim, value)] = isolationKey;
            }
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ChannelIdentityRegistration>> GetIdentitiesAsync(string isolationKey, CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(isolationKey);
        lock (this._gate)
        {
            if (this._keyToIdentities.TryGetValue(isolationKey, out var list))
            {
                return new((IReadOnlyList<ChannelIdentityRegistration>)list.ToArray());
            }
            return new(Array.Empty<ChannelIdentityRegistration>());
        }
    }

    /// <inheritdoc />
    public ValueTask<string?> LookupByVerifiedClaimAsync(string claim, string value, CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(claim);
        Throw.IfNull(value);
        lock (this._gate)
        {
            return new(this._claimToKey.TryGetValue((claim, value), out var key) ? key : null);
        }
    }

    /// <inheritdoc />
    public ValueTask SaveLinkGrantAsync(LinkGrant grant, CancellationToken cancellationToken)
    {
        Throw.IfNull(grant);
        this._linkGrants[grant.Code] = grant;
        return default;
    }

    /// <inheritdoc />
    public ValueTask<LinkGrant?> GetLinkGrantAsync(string code, CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(code);
        if (this._linkGrants.TryGetValue(code, out var grant) && grant.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return new(grant);
        }
        return new((LinkGrant?)null);
    }

    /// <inheritdoc />
    public ValueTask<LinkGrant?> ConsumeLinkGrantAsync(string code, CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(code);
        if (this._linkGrants.TryRemove(code, out var grant) && grant.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return new(grant);
        }
        return new((LinkGrant?)null);
    }

    /// <inheritdoc />
    public ValueTask RecordLastSeenAsync(
        string isolationKey,
        ChannelIdentity identity,
        string? conversationId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(isolationKey);
        Throw.IfNull(identity);
        this._lastSeen[isolationKey] = new LastSeenRecord(identity, conversationId, at);
        return default;
    }

    /// <inheritdoc />
    public ValueTask<LastSeenRecord?> GetLastSeenAsync(string isolationKey, CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(isolationKey);
        return new(this._lastSeen.TryGetValue(isolationKey, out var rec) ? rec : null);
    }

    /// <inheritdoc />
    public ValueTask SaveContinuationAsync(ContinuationToken token, CancellationToken cancellationToken)
    {
        Throw.IfNull(token);
        this._continuations[token.Token] = token;
        return default;
    }

    /// <inheritdoc />
    public ValueTask<ContinuationToken?> GetContinuationAsync(string token, CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(token);
        return new(this._continuations.TryGetValue(token, out var t) ? t : null);
    }

    /// <inheritdoc />
    public ValueTask DeleteContinuationAsync(string token, CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(token);
        this._continuations.TryRemove(token, out _);
        return default;
    }

    /// <inheritdoc />
    public ValueTask RotateSessionAliasAsync(string isolationKey, CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(isolationKey);
        this._sessionAliases[isolationKey] = Guid.NewGuid().ToString("N");
        return default;
    }

    /// <inheritdoc />
    public ValueTask<string?> GetActiveSessionAliasAsync(string isolationKey, CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(isolationKey);
        if (!this._sessionAliases.TryGetValue(isolationKey, out var alias))
        {
            alias = isolationKey;
            this._sessionAliases.TryAdd(isolationKey, alias);
        }
        return new(alias);
    }

    private List<ChannelIdentityRegistration> GetOrCreateList(string isolationKey)
    {
        if (!this._keyToIdentities.TryGetValue(isolationKey, out var list))
        {
            list = [];
            this._keyToIdentities[isolationKey] = list;
        }
        return list;
    }
}