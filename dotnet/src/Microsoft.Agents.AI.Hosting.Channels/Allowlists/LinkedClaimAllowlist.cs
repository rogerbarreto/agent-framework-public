// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Admits identities where a verified claim matches one of the configured values. Supports
/// glob-style wildcards (<c>*@contoso.com</c>) on values. Abstains at <see cref="AuthorizationPhase.PreLink"/>
/// when claims aren't yet available; the host's pipeline triggers the linker accordingly.
/// </summary>
public sealed class LinkedClaimAllowlist : IIdentityAllowlist
{
    private readonly string _claim;
    private readonly string[] _values;
    private readonly Regex[] _patterns;

    /// <summary>Initializes a new instance.</summary>
    public LinkedClaimAllowlist(string claim, params string[] values) : this(claim, (IEnumerable<string>)values)
    {
    }

    /// <summary>Initializes a new instance.</summary>
    public LinkedClaimAllowlist(string claim, IEnumerable<string> values)
    {
        this._claim = Throw.IfNullOrEmpty(claim);
        this._values = (values ?? throw new ArgumentNullException(nameof(values))).ToArray();
        this._patterns = this._values.Select(GlobToRegex).ToArray();
    }

    /// <inheritdoc />
    public bool RequiresLinkedClaims => true;

    /// <inheritdoc />
    public ValueTask<AllowlistDecision> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        Throw.IfNull(context);
        if (!context.VerifiedClaims.TryGetValue(this._claim, out var observed))
        {
            return new(AllowlistDecision.Abstain);
        }

        for (var i = 0; i < this._patterns.Length; i++)
        {
            if (this._patterns[i].IsMatch(observed))
            {
                return new(AllowlistDecision.Allow);
            }
        }
        return new(AllowlistDecision.Deny);
    }

    private static Regex GlobToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".");
        return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}