// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;

namespace Microsoft.Agents.AI.Hosting.UnitTests;

/// <summary>
/// Unit tests for the <see cref="HostedAgentState"/> class.
/// </summary>
public class HostedAgentStateTests
{
    private readonly Mock<AIAgent> _agentMock = new();
    private readonly Mock<AgentSessionStore> _storeMock = new();
    private readonly AgentSession _session = new TestAgentSession();

    [Fact]
    public void Constructor_NullAgent_Throws() =>
        // Act & Assert
        Assert.Throws<ArgumentNullException>("agent", () => new HostedAgentState(null!));

    [Fact]
    public async Task Constructor_NullStore_UsesInMemoryStoreAsync()
    {
        // Arrange: a null store must fall back to an in-memory store, whose create-on-miss path calls the agent.
        this._agentMock
            .Protected()
            .Setup<ValueTask<AgentSession>>("CreateSessionCoreAsync", ItExpr.IsAny<CancellationToken>())
            .Returns(new ValueTask<AgentSession>(this._session));
        var state = new HostedAgentState(this._agentMock.Object);

        // Act
        var session = await state.GetOrCreateSessionAsync("session-1");

        // Assert
        Assert.Same(this._session, session);
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_DelegatesToStoreWithAgentAndIdAsync()
    {
        // Arrange
        this._storeMock
            .Setup(x => x.GetSessionAsync(It.IsAny<AIAgent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(this._session);
        var state = new HostedAgentState(this._agentMock.Object, this._storeMock.Object);

        // Act
        var result = await state.GetOrCreateSessionAsync("session-1");

        // Assert
        Assert.Same(this._session, result);
        this._storeMock.Verify(x => x.GetSessionAsync(this._agentMock.Object, "session-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveSessionAsync_DelegatesToStoreWithAgentAndIdAsync()
    {
        // Arrange
        this._storeMock
            .Setup(x => x.SaveSessionAsync(It.IsAny<AIAgent>(), It.IsAny<string>(), It.IsAny<AgentSession>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        var state = new HostedAgentState(this._agentMock.Object, this._storeMock.Object);

        // Act
        await state.SaveSessionAsync("resp-new", this._session);

        // Assert
        this._storeMock.Verify(x => x.SaveSessionAsync(this._agentMock.Object, "resp-new", this._session, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteSessionAsync_DelegatesToStoreWithAgentAndIdAsync()
    {
        // Arrange
        this._storeMock
            .Setup(x => x.DeleteSessionAsync(It.IsAny<AIAgent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        var state = new HostedAgentState(this._agentMock.Object, this._storeMock.Object);

        // Act
        await state.DeleteSessionAsync("session-1");

        // Assert
        this._storeMock.Verify(x => x.DeleteSessionAsync(this._agentMock.Object, "session-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetOrCreateSessionAsync_InvalidId_ThrowsAsync(string? sessionId)
    {
        // Arrange
        var state = new HostedAgentState(this._agentMock.Object, this._storeMock.Object);

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(() => state.GetOrCreateSessionAsync(sessionId!).AsTask());
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_SerializesConcurrentSameSessionAsync()
    {
        // Arrange: a store that signals when it is entered and blocks until released.
        var store = new GatedStore();
        var state = new HostedAgentState(this._agentMock.Object, store);

        // Act: start a first get-or-create and wait until it is inside the store.
        var first = state.GetOrCreateSessionAsync("session-1").AsTask();
        await store.EnteredSignal.WaitAsync();

        // A second get-or-create for the SAME id must not enter the store while the first holds the lock.
        var second = state.GetOrCreateSessionAsync("session-1").AsTask();

        // Assert: the second call is blocked (no second entry within the wait window).
        Assert.False(await store.EnteredSignal.WaitAsync(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(1, store.EnterCount);

        // Release the first; the second then enters and both complete.
        store.Release.SetResult(true);
        await Task.WhenAll(first, second);
        Assert.Equal(2, store.EnterCount);

        // The per-session lock is reclaimed once no caller holds it.
        Assert.Equal(0, state.ActiveSessionLockCount);
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_DifferentSessions_DoNotBlockEachOtherAsync()
    {
        // Arrange
        var store = new GatedStore();
        var state = new HostedAgentState(this._agentMock.Object, store);

        // Act: a first call holds session-a inside the store.
        var first = state.GetOrCreateSessionAsync("session-a").AsTask();
        await store.EnteredSignal.WaitAsync();

        // A call for a DIFFERENT id must not be blocked by the first (locking is per session id).
        var second = state.GetOrCreateSessionAsync("session-b").AsTask();

        // Assert: the second call enters the store even though the first is still blocked.
        Assert.True(await store.EnteredSignal.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(2, store.EnterCount);

        store.Release.SetResult(true);
        await Task.WhenAll(first, second);
        Assert.Equal(0, state.ActiveSessionLockCount);
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_ReclaimsLockAfterUseAsync()
    {
        // Arrange
        this._storeMock
            .Setup(s => s.GetSessionAsync(It.IsAny<AIAgent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AgentSession>(this._session));
        var state = new HostedAgentState(this._agentMock.Object, this._storeMock.Object);

        // Act
        _ = await state.GetOrCreateSessionAsync("session-1");

        // Assert: the lock table does not retain an entry once the call completes.
        Assert.Equal(0, state.ActiveSessionLockCount);
    }

    private sealed class GatedStore : AgentSessionStore
    {
        public SemaphoreSlim EnteredSignal { get; } = new(0);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int EnterCount;

        public override async ValueTask<AgentSession> GetSessionAsync(AIAgent agent, string sessionStoreId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref this.EnterCount);
            this.EnteredSignal.Release();
            await this.Release.Task.ConfigureAwait(false);
            return new TestAgentSession();
        }

        public override ValueTask SaveSessionAsync(AIAgent agent, string sessionStoreId, AgentSession session, CancellationToken cancellationToken = default)
            => default;

        public override ValueTask DeleteSessionAsync(AIAgent agent, string sessionStoreId, CancellationToken cancellationToken = default)
            => default;
    }

    private sealed class TestAgentSession : AgentSession;
}
