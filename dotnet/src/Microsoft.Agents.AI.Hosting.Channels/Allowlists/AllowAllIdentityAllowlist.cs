// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>Allowlist that admits every identity.</summary>
public sealed class AllowAllIdentityAllowlist : IIdentityAllowlist
{
    /// <summary>Shared singleton.</summary>
    public static AllowAllIdentityAllowlist Instance { get; } = new();

    /// <inheritdoc />
    public ValueTask<AllowlistDecision> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken) =>
        new(AllowlistDecision.Allow);
}