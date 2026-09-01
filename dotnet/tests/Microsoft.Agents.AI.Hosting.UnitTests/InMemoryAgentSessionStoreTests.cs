// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Moq;

namespace Microsoft.Agents.AI.Hosting.UnitTests;

/// <summary>
/// Unit tests for the in-box session stores.
/// </summary>
public class InMemoryAgentSessionStoreTests
{
    [Fact]
    public async Task GetSessionAsync_MissingSession_ReturnsNullAsync()
    {
        // Arrange
        var store = new InMemoryAgentSessionStore();
        var agent = new Mock<AIAgent>();

        // Act
        AgentSession? session = await store.GetSessionAsync(agent.Object, "missing", userId: null);

        // Assert
        Assert.Null(session);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetSessionAsync_BlankUserId_ThrowsAsync(string userId)
    {
        // Arrange
        var store = new InMemoryAgentSessionStore();
        var agent = new Mock<AIAgent>();

        // Act and assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.GetSessionAsync(agent.Object, "conversation-1", userId).AsTask());
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsIndependentSnapshot_ForConcurrentBranchesAsync()
    {
        // Arrange: a real agent so the store round-trips the session through genuine serialize/deserialize,
        // and a stored session that carries some state to copy.
        AIAgent agent = new ChatClientAgent(new NotInvokedChatClient(), name: "assistant");
        var store = new InMemoryAgentSessionStore();

        AgentSession original = await agent.CreateSessionAsync();
        original.StateBag.SetValue("marker", "v1");
        await store.SaveSessionAsync(agent, "s1", original, userId: "user-1");

        // Act: two concurrent branches read the same stored id.
        AgentSession? branchA = await store.GetSessionAsync(agent, "s1", userId: "user-1");
        AgentSession? branchB = await store.GetSessionAsync(agent, "s1", userId: "user-1");

        // Assert: each branch is an independent instance carrying the same content.
        Assert.NotNull(branchA);
        Assert.NotNull(branchB);
        Assert.NotSame(branchA, branchB);
        Assert.Equal("v1", branchA.StateBag.GetValue<string>("marker"));
        Assert.Equal("v1", branchB.StateBag.GetValue<string>("marker"));

        // Mutating one branch must not affect the other branch or the stored snapshot.
        branchA.StateBag.SetValue("marker", "mutated");
        Assert.Equal("v1", branchB.StateBag.GetValue<string>("marker"));

        AgentSession? branchC = await store.GetSessionAsync(agent, "s1", userId: "user-1");
        Assert.NotNull(branchC);
        Assert.Equal("v1", branchC.StateBag.GetValue<string>("marker"));
    }

    [Fact]
    public async Task GetSessionAsync_DifferentUsers_AreIsolatedAsync()
    {
        // Arrange
        AIAgent agent = new ChatClientAgent(new NotInvokedChatClient(), name: "assistant");
        var store = new InMemoryAgentSessionStore();
        AgentSession session = await agent.CreateSessionAsync();
        session.StateBag.SetValue("marker", "user-1");
        await store.SaveSessionAsync(agent, "s1", session, userId: "user-1");

        // Act
        AgentSession? matchingUser = await store.GetSessionAsync(agent, "s1", userId: "user-1");
        AgentSession? differentUser = await store.GetSessionAsync(agent, "s1", userId: "user-2");

        // Assert
        Assert.NotNull(matchingUser);
        Assert.Equal("user-1", matchingUser.StateBag.GetValue<string>("marker"));
        Assert.Null(differentUser);
    }

    // A chat client that is never invoked: these tests only create, serialize, and deserialize sessions.
    private sealed class NotInvokedChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
