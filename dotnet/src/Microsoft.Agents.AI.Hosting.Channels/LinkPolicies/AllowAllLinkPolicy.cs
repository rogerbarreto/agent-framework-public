// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>Permits every link and delivery. Default policy.</summary>
public sealed class AllowAllLinkPolicy : ILinkPolicy
{
    /// <summary>Shared singleton.</summary>
    public static AllowAllLinkPolicy Instance { get; } = new();

    /// <inheritdoc />
    public ValueTask<bool> EvaluateAsync(LinkPolicyContext context, CancellationToken cancellationToken) => new(true);
}