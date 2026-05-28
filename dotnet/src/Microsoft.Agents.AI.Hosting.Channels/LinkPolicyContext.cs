// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Context passed to <see cref="ILinkPolicy.EvaluateAsync"/>.
/// </summary>
public sealed record LinkPolicyContext
{
    /// <summary>The originating channel.</summary>
    public required Channel Source { get; init; }

    /// <summary>The candidate destination channel.</summary>
    public required Channel Destination { get; init; }

    /// <summary>Whether the request is to share an isolation key or to deliver a response.</summary>
    public required LinkPolicyOperation Operation { get; init; }
}