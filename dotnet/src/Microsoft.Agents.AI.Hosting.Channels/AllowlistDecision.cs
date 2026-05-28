// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Tri-state outcome of an <see cref="IIdentityAllowlist"/> evaluation.
/// </summary>
public enum AllowlistDecision
{
    /// <summary>Defer to subsequent allowlists in the combinator chain, or to the default-open rule when none remain.</summary>
    Abstain,

    /// <summary>Admit the identity.</summary>
    Allow,

    /// <summary>Reject the identity. Deny wins over Allow in <see cref="AllOfIdentityAllowlist"/>.</summary>
    Deny,
}