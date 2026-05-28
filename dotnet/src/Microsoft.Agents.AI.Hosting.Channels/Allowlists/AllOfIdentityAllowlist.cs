// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// AND combinator: every child must <see cref="AllowlistDecision.Allow"/>. Any
/// <see cref="AllowlistDecision.Deny"/> wins; one or more <see cref="AllowlistDecision.Abstain"/>
/// with no denials yields <see cref="AllowlistDecision.Abstain"/>.
/// </summary>
public sealed class AllOfIdentityAllowlist : IIdentityAllowlist
{
    private readonly IIdentityAllowlist[] _children;

    /// <summary>Initializes a new instance.</summary>
    public AllOfIdentityAllowlist(params IIdentityAllowlist[] children) : this((IEnumerable<IIdentityAllowlist>)children)
    {
    }

    /// <summary>Initializes a new instance.</summary>
    public AllOfIdentityAllowlist(IEnumerable<IIdentityAllowlist> children)
    {
        Throw.IfNull(children);
        this._children = children.ToArray();
    }

    /// <inheritdoc />
    public bool RequiresLinkedClaims => this._children.Any(c => c.RequiresLinkedClaims);

    /// <inheritdoc />
    public async ValueTask<AllowlistDecision> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        var sawAbstain = false;
        for (var i = 0; i < this._children.Length; i++)
        {
            var decision = await this._children[i].EvaluateAsync(context, cancellationToken).ConfigureAwait(false);
            switch (decision)
            {
                case AllowlistDecision.Deny: return AllowlistDecision.Deny;
                case AllowlistDecision.Abstain: sawAbstain = true; break;
            }
        }
        return sawAbstain ? AllowlistDecision.Abstain : AllowlistDecision.Allow;
    }
}