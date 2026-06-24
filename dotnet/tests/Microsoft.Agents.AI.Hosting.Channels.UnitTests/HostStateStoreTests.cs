// Copyright (c) Microsoft. All rights reserved.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting.Channels.UnitTests;

public class HostStateStoreTests
{
    [Fact]
    public async Task InMemory_GetActiveAlias_StableUntilRotatedAsync()
    {
        // Arrange
        var store = new InMemoryHostStateStore();

        // Act
        var first = await store.GetActiveSessionAliasAsync("user:alice", CancellationToken.None);
        var second = await store.GetActiveSessionAliasAsync("user:alice", CancellationToken.None);
        await store.RotateSessionAliasAsync("user:alice", CancellationToken.None);
        var rotated = await store.GetActiveSessionAliasAsync("user:alice", CancellationToken.None);

        // Assert
        Assert.Equal(first, second);
        Assert.NotEqual(first, rotated);
    }

    [Fact]
    public async Task InMemory_CheckpointLocation_IsDeterministicPerKeyAsync()
    {
        // Arrange
        var store = new InMemoryHostStateStore();

        // Act
        var a1 = await store.GetCheckpointLocationAsync("k1", CancellationToken.None);
        var a2 = await store.GetCheckpointLocationAsync("k1", CancellationToken.None);
        var b = await store.GetCheckpointLocationAsync("k2", CancellationToken.None);

        // Assert
        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
    }

    [Fact]
    public async Task File_Alias_PersistsAcrossInstancesAsync()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "afhost-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store1 = new FileHostStateStore(new HostStatePathOptions { Root = root });
            await store1.RotateSessionAliasAsync("user:bob", CancellationToken.None);
            var alias1 = await store1.GetActiveSessionAliasAsync("user:bob", CancellationToken.None);

            // Act: a fresh instance over the same directory
            var store2 = new FileHostStateStore(new HostStatePathOptions { Root = root });
            var alias2 = await store2.GetActiveSessionAliasAsync("user:bob", CancellationToken.None);

            // Assert
            Assert.Equal(alias1, alias2);
        }
        finally
        {
            if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
        }
    }
}
