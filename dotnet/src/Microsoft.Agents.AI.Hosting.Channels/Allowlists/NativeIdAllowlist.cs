// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Admits identities whose <see cref="ChannelIdentity.Channel"/> matches a target channel and
/// whose <see cref="ChannelIdentity.NativeId"/> is in the allowed set. Abstains on identities
/// from other channels so combinators can defer to peers.
/// </summary>
public sealed class NativeIdAllowlist : IIdentityAllowlist
{
    private readonly string _channel;
    private readonly HashSet<string> _nativeIds;

    /// <summary>Initializes a new instance.</summary>
    public NativeIdAllowlist(string channel, IEnumerable<string> nativeIds)
    {
        this._channel = Throw.IfNullOrEmpty(channel);
        this._nativeIds = new HashSet<string>(nativeIds ?? throw new ArgumentNullException(nameof(nativeIds)), StringComparer.Ordinal);
    }

    /// <summary>The channel this allowlist applies to.</summary>
    public string Channel => this._channel;

    /// <inheritdoc />
    public ValueTask<AllowlistDecision> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        Throw.IfNull(context);
        if (!string.Equals(context.Identity.Channel, this._channel, StringComparison.Ordinal))
        {
            return new(AllowlistDecision.Abstain);
        }
        return new(this._nativeIds.Contains(context.Identity.NativeId) ? AllowlistDecision.Allow : AllowlistDecision.Deny);
    }
}