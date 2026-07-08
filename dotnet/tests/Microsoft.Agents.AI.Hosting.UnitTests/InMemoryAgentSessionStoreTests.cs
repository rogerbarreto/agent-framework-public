// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;

namespace Microsoft.Agents.AI.Hosting.UnitTests;

/// <summary>
/// Unit tests for <see cref="AgentSessionStore.DeleteSessionAsync"/> across the in-box stores.
/// </summary>
public class InMemoryAgentSessionStoreTests
{
    [Fact]
    public async Task DeleteSessionAsync_RemovesStoredSession_SoNextGetCreatesAsync()
    {
        // Arrange
        var stored = JsonSerializer.SerializeToElement(new { marker = "stored" });
        var restoredSession = new TestAgentSession();
        var createdSession = new TestAgentSession();
        var agent = new Mock<AIAgent>();
        agent.Protected()
            .Setup<ValueTask<JsonElement>>("SerializeSessionCoreAsync", ItExpr.IsAny<AgentSession>(), ItExpr.IsAny<JsonSerializerOptions>(), ItExpr.IsAny<CancellationToken>())
            .Returns(new ValueTask<JsonElement>(stored));
        agent.Protected()
            .Setup<ValueTask<AgentSession>>("DeserializeSessionCoreAsync", ItExpr.IsAny<JsonElement>(), ItExpr.IsAny<JsonSerializerOptions>(), ItExpr.IsAny<CancellationToken>())
            .Returns(new ValueTask<AgentSession>(restoredSession));
        agent.Protected()
            .Setup<ValueTask<AgentSession>>("CreateSessionCoreAsync", ItExpr.IsAny<CancellationToken>())
            .Returns(new ValueTask<AgentSession>(createdSession));

        var store = new InMemoryAgentSessionStore();

        // Act & Assert
        await store.SaveSessionAsync(agent.Object, "s1", new TestAgentSession());
        Assert.Same(restoredSession, await store.GetSessionAsync(agent.Object, "s1"));

        await store.DeleteSessionAsync(agent.Object, "s1");
        Assert.Same(createdSession, await store.GetSessionAsync(agent.Object, "s1"));
    }

    [Fact]
    public async Task DeleteSessionAsync_UnknownId_DoesNotThrowAsync()
    {
        // Arrange
        var store = new InMemoryAgentSessionStore();

        // Act & Assert (no exception)
        await store.DeleteSessionAsync(new Mock<AIAgent>().Object, "missing");
    }

    [Fact]
    public async Task DeleteSessionAsync_NoopStore_CompletesAsync()
    {
        // Arrange
        var store = new NoopAgentSessionStore();

        // Act & Assert (no exception)
        await store.DeleteSessionAsync(new Mock<AIAgent>().Object, "any");
    }

    [Fact]
    public async Task DeleteSessionAsync_BaseDefault_ThrowsNotSupportedAsync()
    {
        // Arrange
        AgentSessionStore store = new ConcreteAgentSessionStore();

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(() => store.DeleteSessionAsync(new Mock<AIAgent>().Object, "any").AsTask());
    }

    private sealed class TestAgentSession : AgentSession;

    private sealed class ConcreteAgentSessionStore : AgentSessionStore
    {
        public override ValueTask SaveSessionAsync(AIAgent agent, string conversationId, AgentSession session, CancellationToken cancellationToken = default)
            => default;

        public override ValueTask<AgentSession> GetSessionAsync(AIAgent agent, string conversationId, CancellationToken cancellationToken = default)
            => new(new TestAgentSession());
    }
}
