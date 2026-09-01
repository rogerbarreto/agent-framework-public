// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;

namespace Microsoft.Agents.AI.Hosting.UnitTests;

/// <summary>
/// Unit tests for <see cref="IsolationKeyScopedAgentSessionStore"/>.
/// </summary>
public class IsolationKeyScopedAgentSessionStoreTests
{
    private const string TestIsolationKey = "test-key";
    private const string TestConversationId = "test-conversation-id";

    private readonly Mock<AgentSessionStore> _innerStoreMock = new();
    private readonly Mock<AIAgent> _agentMock = new();

    [Fact]
    public void RequiresInnerStore()
    {
        // Arrange
        var provider = new TestAgentIsolationKeyProvider(TestIsolationKey);

        // Act and assert
        Assert.Throws<ArgumentNullException>("innerStore", () =>
            new IsolationKeyScopedAgentSessionStore(null!, provider));
    }

    [Fact]
    public async Task GetSessionAsync_PassesConversationAndIsolationKeySeparatelyAsync()
    {
        // Arrange
        var expectedSession = new TestAgentSession();
        this._innerStoreMock
            .Setup(x => x.GetSessionAsync(
                this._agentMock.Object,
                TestConversationId,
                TestIsolationKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSession);
        var store = new IsolationKeyScopedAgentSessionStore(
            this._innerStoreMock.Object,
            new TestAgentIsolationKeyProvider(TestIsolationKey));

        // Act
        AgentSession? session = await store.GetSessionAsync(
            this._agentMock.Object,
            TestConversationId,
            userId: null);

        // Assert
        Assert.Same(expectedSession, session);
        this._innerStoreMock.VerifyAll();
    }

    [Fact]
    public async Task SaveSessionAsync_PassesConversationAndIsolationKeySeparatelyAsync()
    {
        // Arrange
        var session = new TestAgentSession();
        this._innerStoreMock
            .Setup(x => x.SaveSessionAsync(
                this._agentMock.Object,
                TestConversationId,
                session,
                TestIsolationKey,
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        var store = new IsolationKeyScopedAgentSessionStore(
            this._innerStoreMock.Object,
            new TestAgentIsolationKeyProvider(TestIsolationKey));

        // Act
        await store.SaveSessionAsync(
            this._agentMock.Object,
            TestConversationId,
            session,
            userId: null);

        // Assert
        this._innerStoreMock.VerifyAll();
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_PassesIsolationKeyToInnerStoreAsync()
    {
        // Arrange
        var expectedSession = new TestAgentSession();
        this._innerStoreMock
            .Setup(x => x.GetOrCreateSessionAsync(
                this._agentMock.Object,
                TestConversationId,
                TestIsolationKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSession);
        var store = new IsolationKeyScopedAgentSessionStore(
            this._innerStoreMock.Object,
            new TestAgentIsolationKeyProvider(TestIsolationKey));

        // Act
        AgentSession session = await store.GetOrCreateSessionAsync(
            this._agentMock.Object,
            TestConversationId,
            userId: null);

        // Assert
        Assert.Same(expectedSession, session);
        this._innerStoreMock.VerifyAll();
    }

    [Fact]
    public async Task GetSessionAsync_StrictModeWithoutIsolationKey_ThrowsAsync()
    {
        // Arrange
        var store = new IsolationKeyScopedAgentSessionStore(
            this._innerStoreMock.Object,
            new TestAgentIsolationKeyProvider(null),
            new IsolationKeyScopedAgentSessionStoreOptions { Strict = true });

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.GetSessionAsync(this._agentMock.Object, TestConversationId, userId: null).AsTask());

        // Assert
        Assert.Contains("Agent isolation key is required", exception.Message);
    }

    [Fact]
    public async Task SaveSessionAsync_StrictModeWithoutIsolationKey_ThrowsAsync()
    {
        // Arrange
        var store = new IsolationKeyScopedAgentSessionStore(
            this._innerStoreMock.Object,
            new TestAgentIsolationKeyProvider(null),
            new IsolationKeyScopedAgentSessionStoreOptions { Strict = true });

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveSessionAsync(
                this._agentMock.Object,
                TestConversationId,
                new TestAgentSession(),
                userId: null).AsTask());

        // Assert
        Assert.Contains("Agent isolation key is required", exception.Message);
    }

    [Fact]
    public async Task GetSessionAsync_NonStrictModePreservesCallerUserIdWhenKeyIsMissingAsync()
    {
        // Arrange
        const string CallerUserId = "caller-user";
        this._innerStoreMock
            .Setup(x => x.GetSessionAsync(
                this._agentMock.Object,
                TestConversationId,
                CallerUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentSession?)null);
        var store = new IsolationKeyScopedAgentSessionStore(
            this._innerStoreMock.Object,
            new TestAgentIsolationKeyProvider(null),
            new IsolationKeyScopedAgentSessionStoreOptions { Strict = false });

        // Act
        await store.GetSessionAsync(this._agentMock.Object, TestConversationId, CallerUserId);

        // Assert
        this._innerStoreMock.VerifyAll();
    }

    [Fact]
    public async Task GetSessionAsync_IsolationKeyOverridesCallerUserIdAsync()
    {
        // Arrange
        this._innerStoreMock
            .Setup(x => x.GetSessionAsync(
                this._agentMock.Object,
                TestConversationId,
                TestIsolationKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentSession?)null);
        var store = new IsolationKeyScopedAgentSessionStore(
            this._innerStoreMock.Object,
            new TestAgentIsolationKeyProvider(TestIsolationKey));

        // Act
        await store.GetSessionAsync(this._agentMock.Object, TestConversationId, "caller-user");

        // Assert
        this._innerStoreMock.VerifyAll();
    }

    private sealed class TestAgentIsolationKeyProvider(string? key) : AgentIsolationKeyProvider
    {
        public override ValueTask<string?> GetIsolationKeyAsync(CancellationToken cancellationToken = default)
            => new(key);
    }

    private sealed class TestAgentSession : AgentSession;
}
