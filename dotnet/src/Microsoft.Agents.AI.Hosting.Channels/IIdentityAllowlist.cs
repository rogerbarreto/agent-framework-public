// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Decision seam for identity admission. Implementations return <see cref="AllowlistDecision.Allow"/>
/// / <see cref="AllowlistDecision.Deny"/> / <see cref="AllowlistDecision.Abstain"/> at each phase
/// of the authorization pipeline.
/// </summary>
public interface IIdentityAllowlist
{
    /// <summary>
    /// When <see langword="true"/>, the host's startup validator rejects configurations where
    /// neither <c>RequireLink=true</c> nor a claim-emitting channel can deliver the claims this
    /// allowlist needs. Prevents the silent-deny-everyone footgun.
    /// </summary>
    bool RequiresLinkedClaims => false;

    /// <summary>Evaluate the supplied context.</summary>
    ValueTask<AllowlistDecision> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken);
}