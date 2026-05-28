// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Short-circuit OR combinator: first <see cref="AllowlistDecision.Allow"/> wins; otherwise
/// returns <see cref="AllowlistDecision.Deny"/> if any child denied, else <see cref="AllowlistDecision.Abstain"/>.
/// </summary>
public sealed class AnyOfIdentityAllowlist : IIdentityAllowlist
{
    private readonly IIdentityAllowlist[] _children;

    /// <summary>Initializes a new instance.</summary>
    public AnyOfIdentityAllowlist(params IIdentityAllowlist[] children) : this((IEnumerable<IIdentityAllowlist>)children)
    {
    }

    /// <summary>Initializes a new instance.</summary>
    public AnyOfIdentityAllowlist(IEnumerable<IIdentityAllowlist> children)
    {
        Throw.IfNull(children);
        this._children = children.ToArray();
    }

    /// <inheritdoc />
    public bool RequiresLinkedClaims => this._children.Any(c => c.RequiresLinkedClaims);

    /// <inheritdoc />
    public async ValueTask<AllowlistDecision> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        var sawDeny = false;
        for (var i = 0; i < this._children.Length; i++)
        {
            var decision = await this._children[i].EvaluateAsync(context, cancellationToken).ConfigureAwait(false);
            switch (decision)
            {
                case AllowlistDecision.Allow: return AllowlistDecision.Allow;
                case AllowlistDecision.Deny: sawDeny = true; break;
            }
        }
        return sawDeny ? AllowlistDecision.Deny : AllowlistDecision.Abstain;
    }
}