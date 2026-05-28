// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>Refuses every link and delivery. Channels share target only, never sessions.</summary>
public sealed class DenyAllLinkPolicy : ILinkPolicy
{
    /// <summary>Shared singleton.</summary>
    public static DenyAllLinkPolicy Instance { get; } = new();

    /// <inheritdoc />
    public ValueTask<bool> EvaluateAsync(LinkPolicyContext context, CancellationToken cancellationToken) => new(false);
}