// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Decides which channels may share an <see cref="IHostStateStore"/> isolation key
/// (<see cref="LinkPolicyOperation.Link"/>) and which channels may be a <see cref="ResponseTarget"/>
/// for one another (<see cref="LinkPolicyOperation.Deliver"/>).
/// </summary>
public interface ILinkPolicy
{
    /// <summary>Returns <see langword="true"/> if the operation is permitted.</summary>
    ValueTask<bool> EvaluateAsync(LinkPolicyContext context, CancellationToken cancellationToken);
}