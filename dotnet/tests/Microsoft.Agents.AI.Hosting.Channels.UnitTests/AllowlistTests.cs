// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.Channels;

namespace Microsoft.Agents.AI.Hosting.Channels.UnitTests;

public class AllowlistTests
{
    private static AuthorizationContext PreLink(string channel, string nativeId, IReadOnlyDictionary<string, string>? claims = null) => new()
    {
        Identity = new ChannelIdentity(channel, nativeId),
        Phase = AuthorizationPhase.PreLink,
        VerifiedClaims = claims ?? new Dictionary<string, string>(),
    };

    [Fact]
    public async Task NativeIdAllowlist_ChannelMismatch_Abstains()
    {
        // Arrange
        var allow = new NativeIdAllowlist("telegram", ["42"]);

        // Act
        var decision = await allow.EvaluateAsync(PreLink("invocations", "42"), CancellationToken.None);

        // Assert
        Assert.Equal(AllowlistDecision.Abstain, decision);
    }

    [Fact]
    public async Task NativeIdAllowlist_HitsAndMisses()
    {
        // Arrange
        var allow = new NativeIdAllowlist("telegram", ["1", "2"]);

        // Act
        var hit = await allow.EvaluateAsync(PreLink("telegram", "2"), CancellationToken.None);
        var miss = await allow.EvaluateAsync(PreLink("telegram", "99"), CancellationToken.None);

        // Assert
        Assert.Equal(AllowlistDecision.Allow, hit);
        Assert.Equal(AllowlistDecision.Deny, miss);
    }

    [Fact]
    public async Task LinkedClaimAllowlist_AbstainsPreLink_AllowsOnGlobMatch()
    {
        // Arrange
        var allow = new LinkedClaimAllowlist("email", "*@contoso.com");

        // Act
        var pre = await allow.EvaluateAsync(PreLink("telegram", "42"), CancellationToken.None);
        var hit = await allow.EvaluateAsync(PreLink("telegram", "42", new Dictionary<string, string> { ["email"] = "alice@contoso.com" }), CancellationToken.None);
        var miss = await allow.EvaluateAsync(PreLink("telegram", "42", new Dictionary<string, string> { ["email"] = "mallory@example.com" }), CancellationToken.None);

        // Assert
        Assert.Equal(AllowlistDecision.Abstain, pre);
        Assert.Equal(AllowlistDecision.Allow, hit);
        Assert.Equal(AllowlistDecision.Deny, miss);
    }

    [Fact]
    public async Task AnyOf_ShortCircuitsOnFirstAllow_DenyWinsOverAbstain()
    {
        // Arrange
        var nativeMatch = new NativeIdAllowlist("telegram", ["42"]);
        var emailReject = new LinkedClaimAllowlist("email", "*@contoso.com");
        var allow = new AnyOfIdentityAllowlist(nativeMatch, emailReject);

        // Act
        var nativeWin = await allow.EvaluateAsync(PreLink("telegram", "42"), CancellationToken.None);
        var emailMiss = await allow.EvaluateAsync(PreLink("telegram", "99", new Dictionary<string, string> { ["email"] = "mallory@example.com" }), CancellationToken.None);
        var allAbstain = await allow.EvaluateAsync(PreLink("invocations", "99"), CancellationToken.None);

        // Assert
        Assert.Equal(AllowlistDecision.Allow, nativeWin);
        Assert.Equal(AllowlistDecision.Deny, emailMiss);
        Assert.Equal(AllowlistDecision.Abstain, allAbstain);
    }
}