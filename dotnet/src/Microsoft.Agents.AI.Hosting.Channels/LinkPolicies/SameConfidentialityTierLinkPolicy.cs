// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Permits links and deliveries when both channels declare the same
/// <see cref="IConfidentialityTagged.ConfidentialityTier"/>; refuses otherwise. Channels without
/// the tag are treated as single-tier (matching any other untagged channel).
/// </summary>
public sealed class SameConfidentialityTierLinkPolicy : ILinkPolicy
{
    /// <summary>Shared singleton.</summary>
    public static SameConfidentialityTierLinkPolicy Instance { get; } = new();

    /// <inheritdoc />
    public ValueTask<bool> EvaluateAsync(LinkPolicyContext context, CancellationToken cancellationToken)
    {
        Throw.IfNull(context);
        var sourceTier = (context.Source as IConfidentialityTagged)?.ConfidentialityTier;
        var destTier = (context.Destination as IConfidentialityTagged)?.ConfidentialityTier;
        return new(string.Equals(sourceTier, destTier, StringComparison.Ordinal));
    }
}