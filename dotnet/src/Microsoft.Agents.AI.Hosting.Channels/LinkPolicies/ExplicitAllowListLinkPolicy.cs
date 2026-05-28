// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Permits links / deliveries only when the (source, destination) channel pair appears in the
/// configured allow list.
/// </summary>
public sealed class ExplicitAllowListLinkPolicy : ILinkPolicy
{
    private readonly HashSet<(string Source, string Destination)> _allowed;

    /// <summary>Initializes a new instance.</summary>
    public ExplicitAllowListLinkPolicy(IEnumerable<(string Source, string Destination)> allowedPairs)
    {
        Throw.IfNull(allowedPairs);
        this._allowed = new HashSet<(string, string)>(allowedPairs);
    }

    /// <inheritdoc />
    public ValueTask<bool> EvaluateAsync(LinkPolicyContext context, CancellationToken cancellationToken)
    {
        Throw.IfNull(context);
        return new(this._allowed.Contains((context.Source.Name, context.Destination.Name)));
    }
}