// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Discriminated outcome returned by <see cref="AgentFrameworkHost.AuthorizeAsync"/>.
/// </summary>
public abstract record AuthorizationOutcome
{
    private AuthorizationOutcome() { }

    /// <summary>The identity is admitted; <paramref name="IsolationKey"/> is the resolved (or auto-issued) key.</summary>
    public sealed record Allowed(string IsolationKey) : AuthorizationOutcome;

    /// <summary>The identity must complete a link ceremony before access is granted.</summary>
    public sealed record LinkRequired(LinkChallenge Challenge) : AuthorizationOutcome;

    /// <summary>The identity is denied. <paramref name="ReasonCode"/> is stable and machine-readable.</summary>
    /// <param name="ReasonCode">Stable, machine-readable denial code.</param>
    /// <param name="UserMessage">Optional message safe to surface publicly.</param>
    /// <param name="LogDetails">Optional structured detail for logs / telemetry. Never shown to users.</param>
    public sealed record Denied(
        string ReasonCode,
        string? UserMessage = null,
        IReadOnlyDictionary<string, object?>? LogDetails = null) : AuthorizationOutcome;
}