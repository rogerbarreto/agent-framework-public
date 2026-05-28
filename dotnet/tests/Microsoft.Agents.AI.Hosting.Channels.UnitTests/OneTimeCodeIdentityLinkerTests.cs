// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.Channels;

namespace Microsoft.Agents.AI.Hosting.Channels.UnitTests;

public class OneTimeCodeIdentityLinkerTests
{
    [Fact]
    public async Task BeginAndComplete_CollapseTwoIdentitiesOntoOneKey()
    {
        // Arrange
        var store = new InMemoryHostStateStore();
        var linker = new OneTimeCodeIdentityLinker(store);
        var aliceOnInvocations = new ChannelIdentity("invocations", "alice-1");
        var aliceOnTelegram = new ChannelIdentity("telegram", "12345");

        // Act
        var challenge = await linker.BeginAsync(aliceOnInvocations, requestedIsolationKey: "alice", CancellationToken.None);
        var principal = await linker.CompleteAsync(challenge.ChallengeId, new Dictionary<string, object?> { ["identity"] = aliceOnTelegram }, CancellationToken.None);

        var keyOnInvocations = await store.GetIsolationKeyAsync(aliceOnInvocations, CancellationToken.None);
        var keyOnTelegram = await store.GetIsolationKeyAsync(aliceOnTelegram, CancellationToken.None);

        // Assert
        Assert.Equal("alice", principal.IsolationKey);
        Assert.Equal("alice", keyOnInvocations);
        Assert.Equal("alice", keyOnTelegram);
    }

    [Fact]
    public async Task Complete_RejectsUnknownCode()
    {
        // Arrange
        var store = new InMemoryHostStateStore();
        var linker = new OneTimeCodeIdentityLinker(store);
        var alice = new ChannelIdentity("telegram", "12345");

        // Act / Assert
        await Assert.ThrowsAsync<System.InvalidOperationException>(() =>
            linker.CompleteAsync("NOPE", new Dictionary<string, object?> { ["identity"] = alice }, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task IsLinked_AutoMergesOnVerifiedClaim()
    {
        // Arrange
        var store = new InMemoryHostStateStore();
        var linker = new OneTimeCodeIdentityLinker(store);
        var alice = new ChannelIdentity("telegram", "1");
        await store.SaveLinkAsync(alice, "alice", new Dictionary<string, string> { ["email"] = "alice@contoso.com" }, CancellationToken.None);
        var aliceOnInvocations = new ChannelIdentity("invocations", "alice-1");

        // Act
        var resolved = await linker.IsLinkedAsync(aliceOnInvocations, new Dictionary<string, string> { ["email"] = "alice@contoso.com" }, CancellationToken.None);
        var afterMerge = await store.GetIsolationKeyAsync(aliceOnInvocations, CancellationToken.None);

        // Assert
        Assert.Equal("alice", resolved);
        Assert.Equal("alice", afterMerge);
    }
}