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
    public async Task LockSessionAsync_LockingDisabled_ReturnsNoopReleaserAsync()
    {
        // Arrange
        var state = new HostedAgentState(this._agentMock.Object, this._storeMock.Object);

        // Act
        var releaser = await state.LockSessionAsync("session-1");

        // Assert (a second acquire does not block when locking is disabled)
        var second = await state.LockSessionAsync("session-1");
        await releaser.DisposeAsync();
        await second.DisposeAsync();
    }

    [Fact]
    public async Task LockSessionAsync_LockingEnabled_SerializesSameSessionAsync()
    {
        // Arrange
        var state = new HostedAgentState(this._agentMock.Object, this._storeMock.Object, enableSessionLocking: true);
        var first = await state.LockSessionAsync("session-1");

        // Act: a second acquire for the same session must not complete until the first is released.
        var secondTask = state.LockSessionAsync("session-1").AsTask();
        var completedBeforeRelease = secondTask.IsCompleted;
        await first.DisposeAsync();
        var second = await secondTask;
        await second.DisposeAsync();

        // Assert
        Assert.False(completedBeforeRelease);
    }

    [Fact]
    public async Task LockSessionAsync_ReleasingLastHolder_ReclaimsLockEntryAsync()
    {
        // Arrange
        var state = new HostedAgentState(this._agentMock.Object, this._storeMock.Object, enableSessionLocking: true);

        // Act: acquire and release two different sessions, then acquire/release one again.
        var a = await state.LockSessionAsync("session-a");
        var b = await state.LockSessionAsync("session-b");
        Assert.Equal(2, state.ActiveSessionLockCount);

        await a.DisposeAsync();
        await b.DisposeAsync();

        // Assert: the lock table is emptied once every holder releases, so it does not grow unbounded.
        Assert.Equal(0, state.ActiveSessionLockCount);
    }

    [Fact]
    public async Task LockSessionAsync_ConcurrentHolder_KeepsEntryUntilBothReleaseAsync()
    {
        // Arrange
        var state = new HostedAgentState(this._agentMock.Object, this._storeMock.Object, enableSessionLocking: true);

        // Act: first holder owns the lock; a second acquire for the same id is pending (still a live referent).
        var first = await state.LockSessionAsync("session-1");
        var secondTask = state.LockSessionAsync("session-1").AsTask();

        // Assert: the entry survives while a second referent is waiting.
        Assert.Equal(1, state.ActiveSessionLockCount);

        await first.DisposeAsync();
        var second = await secondTask;
        Assert.Equal(1, state.ActiveSessionLockCount);

        await second.DisposeAsync();
        Assert.Equal(0, state.ActiveSessionLockCount);
    }

    [Fact]
    public async Task LockSessionAsync_CancelledAcquire_ReclaimsReservationAsync()
    {
        // Arrange: hold the lock so a second acquire blocks, then cancel that second acquire.
        var state = new HostedAgentState(this._agentMock.Object, this._storeMock.Object, enableSessionLocking: true);
        var holder = await state.LockSessionAsync("session-1");
        using var cts = new CancellationTokenSource();

        // Act
        var pending = state.LockSessionAsync("session-1", cts.Token).AsTask();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        // Assert: the cancelled reservation is dropped; releasing the holder reclaims the entry.
        await holder.DisposeAsync();
        Assert.Equal(0, state.ActiveSessionLockCount);
    }

    private sealed class TestAgentSession : AgentSession;
}
