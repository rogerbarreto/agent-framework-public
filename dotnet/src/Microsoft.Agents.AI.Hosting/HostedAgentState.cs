// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting;

/// <summary>
/// Optional shared execution state for applications that own their own hosting route and want to reuse
/// Agent Framework session continuity. Pairs a single <see cref="AIAgent"/> target with an
/// <see cref="AgentSessionStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// This holder exists because only an object that has both the resolved agent target and the session
/// store can offer a target-aware get-or-create. It does not own routing, authentication, middleware, or
/// storage policy; those remain with the application. It does not replace <see cref="AgentSessionStore"/>,
/// which already provides serialization and per-principal isolation.
/// </para>
/// <para>
/// <strong>Trust boundary.</strong> The <c>sessionStoreId</c> values passed to these methods are
/// application-selected partition keys. When a key originates from the wire (for example via
/// <c>OpenAIResponses.GetSessionId(...)</c>), the application must authenticate the caller and authorize
/// the key before using it here. For multi-user hosts, scope the underlying store per principal with
/// <see cref="IsolationKeyScopedAgentSessionStore"/>.
/// </para>
/// </remarks>
public sealed class HostedAgentState
{
    private readonly AIAgent _agent;
    private readonly AgentSessionStore _sessionStore;

    // Per-session-store-id mutexes for LockSessionAsync. Guarded by _sessionLocksSync for all reads and
    // mutations (including the reference count), so entries can be reclaimed when their last holder releases
    // without racing a concurrent acquirer. Null when session locking is disabled.
    private readonly Dictionary<string, SessionLock>? _sessionLocks;
    private readonly object _sessionLocksSync = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedAgentState"/> class.
    /// </summary>
    /// <param name="agent">The agent target used by route code.</param>
    /// <param name="sessionStore">
    /// The session store to use. Defaults to a fresh <see cref="InMemoryAgentSessionStore"/> when not provided.
    /// </param>
    /// <param name="enableSessionLocking">
    /// When <see langword="true"/>, <see cref="LockSessionAsync"/> serializes access per session id. Defaults
    /// to <see langword="false"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is <see langword="null"/>.</exception>
    public HostedAgentState(AIAgent agent, AgentSessionStore? sessionStore = null, bool enableSessionLocking = false)
    {
        _ = Throw.IfNull(agent);

        this._agent = agent;
        this._sessionStore = sessionStore ?? new InMemoryAgentSessionStore();
        this._sessionLocks = enableSessionLocking ? new Dictionary<string, SessionLock>(StringComparer.Ordinal) : null;
    }

    /// <summary>
    /// Returns the stored session for <paramref name="sessionStoreId"/>, creating a new session on first use.
    /// </summary>
    /// <param name="sessionStoreId">The application-selected id under which the session is stored.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The resolved or newly created <see cref="AgentSession"/>.</returns>
    public ValueTask<AgentSession> GetOrCreateSessionAsync(string sessionStoreId, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNullOrEmpty(sessionStoreId);
        return this._sessionStore.GetSessionAsync(this._agent, sessionStoreId, cancellationToken);
    }

    /// <summary>
    /// Persists <paramref name="session"/> under <paramref name="sessionStoreId"/>. Call this after the run
    /// completes, including under a newly minted continuation id when the protocol mints one.
    /// </summary>
    /// <param name="sessionStoreId">The application-selected id under which the session is stored (may be a newly minted id).</param>
    /// <param name="session">The session to persist.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    public ValueTask SaveSessionAsync(string sessionStoreId, AgentSession session, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNullOrEmpty(sessionStoreId);
        _ = Throw.IfNull(session);
        return this._sessionStore.SaveSessionAsync(this._agent, sessionStoreId, session, cancellationToken);
    }

    /// <summary>
    /// Deletes the stored session for <paramref name="sessionStoreId"/>, if present.
    /// </summary>
    /// <param name="sessionStoreId">The application-selected id under which the session is stored.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous delete operation.</returns>
    /// <exception cref="NotSupportedException">The underlying store does not support deletion.</exception>
    public ValueTask DeleteSessionAsync(string sessionStoreId, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNullOrEmpty(sessionStoreId);
        return this._sessionStore.DeleteSessionAsync(this._agent, sessionStoreId, cancellationToken);
    }

    /// <summary>
    /// Acquires an exclusive lock for <paramref name="sessionStoreId"/> so concurrent requests for the same
    /// session serialize their get-run-save cycle. Dispose the returned value to release the lock.
    /// </summary>
    /// <param name="sessionStoreId">The application-selected id under which the session is stored.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>An <see cref="IAsyncDisposable"/> that releases the lock when disposed.</returns>
    /// <remarks>
    /// When session locking is not enabled, this returns immediately with a no-op releaser. Otherwise each
    /// distinct <paramref name="sessionStoreId"/> gets a reference-counted mutex that is removed once its last
    /// holder releases, so the lock table does not grow unbounded as new session ids arrive.
    /// </remarks>
    public async ValueTask<IAsyncDisposable> LockSessionAsync(string sessionStoreId, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNullOrEmpty(sessionStoreId);

        if (this._sessionLocks is null)
        {
            return NoopReleaser.Instance;
        }

        // Reserve a reference before waiting so a concurrent releaser cannot reclaim the entry from under us.
        SessionLock entry;
        lock (this._sessionLocksSync)
        {
            if (!this._sessionLocks.TryGetValue(sessionStoreId, out entry!))
            {
                entry = new SessionLock();
                this._sessionLocks[sessionStoreId] = entry;
            }

            entry.RefCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The wait was cancelled (or faulted) before we held the lock. Drop our reservation and reclaim
            // the entry if we were the last referent.
            this.ReleaseSessionLock(sessionStoreId, entry, acquired: false);
            throw;
        }

        return new SessionLockReleaser(this, sessionStoreId, entry);
    }

    private void ReleaseSessionLock(string sessionStoreId, SessionLock entry, bool acquired)
    {
        lock (this._sessionLocksSync)
        {
            if (acquired)
            {
                entry.Semaphore.Release();
            }

            if (--entry.RefCount == 0)
            {
                // No holders or waiters remain (every acquirer bumps RefCount before waiting), so it is safe to
                // remove and dispose. A later acquire for the same id simply creates a fresh entry.
                this._sessionLocks!.Remove(sessionStoreId);
                entry.Semaphore.Dispose();
            }
        }
    }

    /// <summary>
    /// Gets the number of active per-session locks currently tracked. Zero when session locking is disabled.
    /// </summary>
    /// <remarks>Internal test hook used to verify that released locks are reclaimed.</remarks>
    internal int ActiveSessionLockCount
    {
        get
        {
            if (this._sessionLocks is null)
            {
                return 0;
            }

            lock (this._sessionLocksSync)
            {
                return this._sessionLocks.Count;
            }
        }
    }

    private sealed class SessionLock
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        // Number of callers that currently hold or are waiting on this lock. Mutated only under
        // HostedAgentState._sessionLocksSync.
        public int RefCount;
    }

    private sealed class SessionLockReleaser(HostedAgentState owner, string sessionStoreId, SessionLock entry) : IAsyncDisposable
    {
        private HostedAgentState? _owner = owner;

        public ValueTask DisposeAsync()
        {
            // Release at most once even if the caller disposes more than once.
            Interlocked.Exchange(ref this._owner, null)?.ReleaseSessionLock(sessionStoreId, entry, acquired: true);
            return default;
        }
    }

    private sealed class NoopReleaser : IAsyncDisposable
    {
        public static readonly NoopReleaser Instance = new();

        public ValueTask DisposeAsync() => default;
    }
}
