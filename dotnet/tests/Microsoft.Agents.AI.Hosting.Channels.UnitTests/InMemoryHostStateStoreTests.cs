// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.Channels;

namespace Microsoft.Agents.AI.Hosting.Channels.UnitTests;

public class InMemoryHostStateStoreTests
{
    [Fact]
    public async Task SaveLink_AndGet_RoundTrips()
    {
        // Arrange
        var store = new InMemoryHostStateStore();
        var alice = new ChannelIdentity("telegram", "1");

        // Act
        await store.SaveLinkAsync(alice, "user:alice", verifiedClaims: null, CancellationToken.None);
        var key = await store.GetIsolationKeyAsync(alice, CancellationToken.None);

        // Assert
        Assert.Equal("user:alice", key);
    }

    [Fact]
    public async Task SaveLink_AtomicallyMergesPriorKey()
    {
        // Arrange
        var store = new InMemoryHostStateStore();
        var alice = new ChannelIdentity("telegram", "1");
        var aliceOnInvocations = new ChannelIdentity("invocations", "alice-1");

        // First registration assigns its own key, then we relink onto the canonical one.
        await store.SaveLinkAsync(alice, "telegram:1", verifiedClaims: null, CancellationToken.None);
        await store.SaveLinkAsync(aliceOnInvocations, "alice", verifiedClaims: null, CancellationToken.None);
        await store.SaveLinkAsync(alice, "alice", verifiedClaims: null, CancellationToken.None);

        // Act
        var aliceKey = await store.GetIsolationKeyAsync(alice, CancellationToken.None);
        var aliceInvKey = await store.GetIsolationKeyAsync(aliceOnInvocations, CancellationToken.None);
        var identities = await store.GetIdentitiesAsync("alice", CancellationToken.None);

        // Assert
        Assert.Equal("alice", aliceKey);
        Assert.Equal("alice", aliceInvKey);
        Assert.Equal(2, identities.Count);
    }

    [Fact]
    public async Task SaveLink_PersistsVerifiedClaimsForLookup()
    {
        // Arrange
        var store = new InMemoryHostStateStore();
        var alice = new ChannelIdentity("telegram", "1");
        await store.SaveLinkAsync(alice, "alice", new System.Collections.Generic.Dictionary<string, string> { ["email"] = "alice@contoso.com" }, CancellationToken.None);

        // Act
        var hit = await store.LookupByVerifiedClaimAsync("email", "alice@contoso.com", CancellationToken.None);
        var miss = await store.LookupByVerifiedClaimAsync("email", "ghost@example.com", CancellationToken.None);

        // Assert
        Assert.Equal("alice", hit);
        Assert.Null(miss);
    }

    [Fact]
    public async Task ConsumeLinkGrant_DeletesEntry()
    {
        // Arrange
        var store = new InMemoryHostStateStore();
        var grant = new LinkGrant("CODE1", "linker", null, System.DateTimeOffset.UtcNow.AddMinutes(5), new System.Collections.Generic.Dictionary<string, object?>());
        await store.SaveLinkGrantAsync(grant, CancellationToken.None);

        // Act
        var first = await store.ConsumeLinkGrantAsync("CODE1", CancellationToken.None);
        var second = await store.ConsumeLinkGrantAsync("CODE1", CancellationToken.None);

        // Assert
        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task RotateSessionAlias_ChangesAlias()
    {
        // Arrange
        var store = new InMemoryHostStateStore();
        var initial = await store.GetActiveSessionAliasAsync("alice", CancellationToken.None);

        // Act
        await store.RotateSessionAliasAsync("alice", CancellationToken.None);
        var rotated = await store.GetActiveSessionAliasAsync("alice", CancellationToken.None);

        // Assert
        Assert.NotEqual(initial, rotated);
    }
}