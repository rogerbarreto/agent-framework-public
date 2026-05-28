// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// State passed to an <see cref="IIdentityAllowlist"/> at each phase of the authorization pipeline.
/// </summary>
public sealed record AuthorizationContext
{
    /// <summary>The channel-native identity being authorized.</summary>
    public required ChannelIdentity Identity { get; init; }

    /// <summary>Current evaluation phase. PreLink runs before any linker is consulted.</summary>
    public required AuthorizationPhase Phase { get; init; }

    /// <summary>Resolved isolation key. <see langword="null"/> at <see cref="AuthorizationPhase.PreLink"/>.</summary>
    public string? IsolationKey { get; init; }

    /// <summary>Verified claims attached to this evaluation.</summary>
    public IReadOnlyDictionary<string, string> VerifiedClaims { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>Origin of <see cref="VerifiedClaims"/>.</summary>
    public ClaimSource ClaimSource { get; init; } = ClaimSource.None;

    /// <summary>Conversation shape hints from the channel.</summary>
    public ConversationContext? ConversationContext { get; init; }
}