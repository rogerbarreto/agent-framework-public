// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;

namespace Microsoft.Agents.AI.Abstractions.UnitTests;

/// <summary>
/// Unit tests for <see cref="AgentSessionStore"/>.
/// </summary>
public sealed class AgentSessionStoreTests
{
    [Fact]
    public async Task GetOrCreateSessionAsync_StoredSession_ReturnsStoredSessionAsync()
    {
        // Arrange
        var storedSession = new TestAgentSession();
        var store = new TestAgentSessionStore(storedSession);
        var agent = new Mock<AIAgent>();

        // Act
        AgentSession session = await store.GetOrCreateSessionAsync(
            agent.Object,
            "conversation-1",
            "user-1");

        // Assert
        Assert.Same(storedSession, session);
        Assert.Equal("conversation-1", store.LastConversationId);
        Assert.Equal("user-1", store.LastUserId);
        agent.Protected().Verify(
            "CreateSessionCoreAsync",
            Times.Never(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_MissingSession_CreatesSessionAsync()
    {
        // Arrange
        var createdSession = new TestAgentSession();
        var store = new TestAgentSessionStore(session: null);
        var agent = new Mock<AIAgent>();
        agent.Protected()
            .Setup<ValueTask<AgentSession>>("CreateSessionCoreAsync", ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(createdSession);

        // Act
        AgentSession session = await store.GetOrCreateSessionAsync(
            agent.Object,
            "conversation-1",
            userId: null);

        // Assert
        Assert.Same(createdSession, session);
        agent.Protected().Verify(
            "CreateSessionCoreAsync",
            Times.Once(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_NullAgent_ThrowsAsync()
    {
        // Arrange
        var store = new TestAgentSessionStore(session: null);

        // Act and assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => store.GetOrCreateSessionAsync(null!, "conversation-1", userId: null).AsTask());
    }

    private sealed class TestAgentSessionStore(AgentSession? session) : AgentSessionStore
    {
        public string? LastConversationId { get; private set; }

        public string? LastUserId { get; private set; }

        public override ValueTask<AgentSession?> GetSessionAsync(
            AIAgent agent,
            string conversationId,
            string? userId,
            CancellationToken cancellationToken = default)
        {
            this.LastConversationId = conversationId;
            this.LastUserId = userId;
            return new(session);
        }

        public override ValueTask SaveSessionAsync(
            AIAgent agent,
            string conversationId,
            AgentSession session,
            string? userId,
            CancellationToken cancellationToken = default)
            => default;
    }

    private sealed class TestAgentSession : AgentSession;
}
