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

    // Per-session-store-id mutexes that serialize create-on-miss inside GetOrCreateSessionAsync. Guarded by
    // _sessionLocksSync for all reads and mutations (including the reference count), so entries are reclaimed
    // once their last user finishes without racing a concurrent one.
    private readonly Dictionary<string, SessionLock> _sessionLocks = new(StringComparer.Ordinal);
    private readonly object _sessionLocksSync = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedAgentState"/> class.
    /// </summary>
    /// <param name="agent">The agent target used by route code.</param>
    /// <param name="sessionStore">
    /// The session store to use. Defaults to a fresh <see cref="InMemoryAgentSessionStore"/> when not provided.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is <see langword="null"/>.</exception>
    public HostedAgentState(AIAgent agent, AgentSessionStore? sessionStore = null)
    {
        _ = Throw.IfNull(agent);

        this._agent = agent;
        this._sessionStore = sessionStore ?? new InMemoryAgentSessionStore();
    }

    /// <summary>
    /// Returns the stored session for <paramref name="sessionStoreId"/>, creating a new session on first use.
    /// </summary>
    /// <param name="sessionStoreId">The application-selected id under which the session is stored.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The resolved or newly created <see cref="AgentSession"/>.</returns>
    /// <remarks>
    /// Concurrent calls for the same <paramref name="sessionStoreId"/> are serialized internally so create-on-miss
    /// stays consistent. The locking is automatic and internal; callers never manage it.
    /// </remarks>
    public async ValueTask<AgentSession> GetOrCreateSessionAsync(string sessionStoreId, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNullOrEmpty(sessionStoreId);

        SessionLock entry = this.RentSessionLock(sessionStoreId);
        bool acquired = false;
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            return await this._sessionStore.GetSessionAsync(this._agent, sessionStoreId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this.ReleaseSessionLock(sessionStoreId, entry, acquired);
        }
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

    // Reserves a reference to the per-session lock, creating it on first use. Callers must pair this with
    // ReleaseSessionLock. Reference counting lets the entry be reclaimed once no caller holds or waits on it.
    private SessionLock RentSessionLock(string sessionStoreId)
    {
        lock (this._sessionLocksSync)
        {
            if (!this._sessionLocks.TryGetValue(sessionStoreId, out SessionLock? entry))
            {
                entry = new SessionLock();
                this._sessionLocks[sessionStoreId] = entry;
            }

            entry.RefCount++;
            return entry;
        }
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
                // No user holds or waits on this lock (every user bumps RefCount before waiting), so it is safe
                // to remove and dispose. A later call for the same id simply creates a fresh entry. This keeps
                // the lock table from growing unbounded as new session ids arrive.
                this._sessionLocks.Remove(sessionStoreId);
                entry.Semaphore.Dispose();
            }
        }
    }

    /// <summary>
    /// Gets the number of active per-session locks currently tracked.
    /// </summary>
    /// <remarks>Internal test hook used to verify that per-session locks are reclaimed.</remarks>
    internal int ActiveSessionLockCount
    {
        get
        {
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
}
