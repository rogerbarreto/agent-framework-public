// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Persistence seam for host-execution metadata that outlives a single request: continuation
/// tokens, identity registry, identity-link grants, and last-seen ledger. Separate from
/// <c>AgentSessionStore</c> (per-conversation history) and <c>WorkflowBuilder.CheckpointStorage</c>
/// (workflow checkpoints).
/// </summary>
public interface IHostStateStore
{
    // ---- Identity registry ----

    /// <summary>Resolve the isolation key for a channel-native identity. <see langword="null"/> if unknown.</summary>
    ValueTask<string?> GetIsolationKeyAsync(ChannelIdentity identity, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically map (or merge) <paramref name="identity"/> onto <paramref name="isolationKey"/>.
    /// If the identity already maps to a different isolation key, both keys' records are merged.
    /// Optional <paramref name="verifiedClaims"/> are persisted alongside for future auto-link replay.
    /// </summary>
    ValueTask SaveLinkAsync(
        ChannelIdentity identity,
        string isolationKey,
        IReadOnlyDictionary<string, string>? verifiedClaims,
        CancellationToken cancellationToken);

    /// <summary>Enumerate every identity mapped to <paramref name="isolationKey"/>.</summary>
    ValueTask<IReadOnlyList<ChannelIdentityRegistration>> GetIdentitiesAsync(
        string isolationKey,
        CancellationToken cancellationToken);

    /// <summary>Look up an isolation key by a verified claim value.</summary>
    ValueTask<string?> LookupByVerifiedClaimAsync(
        string claim,
        string value,
        CancellationToken cancellationToken);

    // ---- Link grants ----

    /// <summary>Persist a pending link grant (one-time code, OAuth state).</summary>
    ValueTask SaveLinkGrantAsync(LinkGrant grant, CancellationToken cancellationToken);

    /// <summary>Read an unexpired link grant.</summary>
    ValueTask<LinkGrant?> GetLinkGrantAsync(string code, CancellationToken cancellationToken);

    /// <summary>Atomically read-and-delete a link grant.</summary>
    ValueTask<LinkGrant?> ConsumeLinkGrantAsync(string code, CancellationToken cancellationToken);

    // ---- Last seen ----

    /// <summary>Record that <paramref name="identity"/> was last seen at <paramref name="at"/>.</summary>
    ValueTask RecordLastSeenAsync(
        string isolationKey,
        ChannelIdentity identity,
        string? conversationId,
        DateTimeOffset at,
        CancellationToken cancellationToken);

    /// <summary>Read the latest last-seen entry for an isolation key.</summary>
    ValueTask<LastSeenRecord?> GetLastSeenAsync(string isolationKey, CancellationToken cancellationToken);

    // ---- Continuation tokens ----

    /// <summary>Persist (or replace) a continuation token.</summary>
    ValueTask SaveContinuationAsync(ContinuationToken token, CancellationToken cancellationToken);

    /// <summary>Read a continuation token by its opaque string identifier.</summary>
    ValueTask<ContinuationToken?> GetContinuationAsync(string token, CancellationToken cancellationToken);

    /// <summary>Delete a continuation token.</summary>
    ValueTask DeleteContinuationAsync(string token, CancellationToken cancellationToken);

    // ---- Session alias rotation ----

    /// <summary>
    /// Rotate the active session-id alias for an isolation key. Backs
    /// <see cref="AgentFrameworkHost.ResetSessionAsync"/> for host-tracked channels' <c>/new</c>-style commands.
    /// </summary>
    ValueTask RotateSessionAliasAsync(string isolationKey, CancellationToken cancellationToken);

    /// <summary>Read the active session-id alias for an isolation key.</summary>
    ValueTask<string?> GetActiveSessionAliasAsync(string isolationKey, CancellationToken cancellationToken);
}