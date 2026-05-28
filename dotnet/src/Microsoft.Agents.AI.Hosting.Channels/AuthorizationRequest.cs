// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Channel-supplied parameters for an <see cref="AgentFrameworkHost.AuthorizeAsync"/> call.
/// </summary>
public sealed record AuthorizationRequest
{
    /// <summary>
    /// When <see langword="true"/>, the host forces a link ceremony even when no allowlist requires it.
    /// </summary>
    public bool RequireLink { get; init; }

    /// <summary>
    /// Per-call allowlist override. <see langword="null"/> means "use the host default".
    /// </summary>
    public IIdentityAllowlist? Allowlist { get; init; }

    /// <summary>
    /// Verified claims the channel observed for this identity (e.g. AAD object id from a bearer token).
    /// </summary>
    public IReadOnlyDictionary<string, string>? VerifiedClaims { get; init; }

    /// <summary>Conversation shape hints (group vs. 1:1).</summary>
    public ConversationContext? ConversationContext { get; init; }
}