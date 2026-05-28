// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Ceremony seam for binding a channel-native identity to verified IdP claims and a host
/// isolation key. Implementations may publish callback routes via <see cref="Contribute"/>.
/// </summary>
public interface IIdentityLinker
{
    /// <summary>Stable linker name (used in log details and startup validation messages).</summary>
    string Name { get; }

    /// <summary>Linker-supplied routes (e.g. OAuth callback). Same shape as <see cref="Channel.Contribute"/>.</summary>
    ChannelContribution Contribute(IChannelContext context);

    /// <summary>Begin a link ceremony for the given identity.</summary>
    ValueTask<LinkChallenge> BeginAsync(
        ChannelIdentity identity,
        string? requestedIsolationKey,
        CancellationToken cancellationToken);

    /// <summary>Complete a previously-issued challenge.</summary>
    ValueTask<PrincipalIdentity> CompleteAsync(
        string challengeId,
        IReadOnlyDictionary<string, object?> proof,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the isolation key for an already-linked identity, or <see langword="null"/> if no
    /// link exists. When <paramref name="verifiedClaims"/> entries match existing link records
    /// the linker auto-merges <paramref name="identity"/> onto the existing isolation key.
    /// </summary>
    ValueTask<string?> IsLinkedAsync(
        ChannelIdentity identity,
        IReadOnlyDictionary<string, string>? verifiedClaims,
        CancellationToken cancellationToken);
}