// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
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
/// <strong>Trust boundary.</strong> The <c>sessionId</c> values passed to these methods are
/// application-selected partition keys. When a key originates from the wire (for example via
/// <c>OpenAIResponses.GetSessionId(...)</c>), the application must authenticate the caller and authorize
/// the key before using it here. For multi-user hosts, scope the underlying store per principal with
/// <see cref="IsolationKeyScopedAgentSessionStore"/>.
/// </para>
/// </remarks>
public sealed class HostedAgentState
{
    private readonly AgentSessionStore _sessionStore;
    private readonly ConcurrentDictionary<string, SemaphoreSlim>? _sessionLocks;

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

        this.Agent = agent;
        this._sessionStore = sessionStore ?? new InMemoryAgentSessionStore();
        this._sessionLocks = enableSessionLocking ? new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal) : null;
    }

    /// <summary>
    /// Gets the agent target.
    /// </summary>
    public AIAgent Agent { get; }

    /// <summary>
    /// Returns the stored session for <paramref name="sessionId"/>, creating a new session on first use.
    /// </summary>
    /// <param name="sessionId">The application-selected session id.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The resolved or newly created <see cref="AgentSession"/>.</returns>
    public ValueTask<AgentSession> GetOrCreateSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNullOrEmpty(sessionId);
        return this._sessionStore.GetSessionAsync(this.Agent, sessionId, cancellationToken);
    }

    /// <summary>
    /// Persists <paramref name="session"/> under <paramref name="sessionId"/>. Call this after the run
    /// completes, including under a newly minted continuation id when the protocol mints one.
    /// </summary>
    /// <param name="sessionId">The application-selected session id (may be a newly minted id).</param>
    /// <param name="session">The session to persist.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    public ValueTask SaveSessionAsync(string sessionId, AgentSession session, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNullOrEmpty(sessionId);
        _ = Throw.IfNull(session);
        return this._sessionStore.SaveSessionAsync(this.Agent, sessionId, session, cancellationToken);
    }

    /// <summary>
    /// Deletes the stored session for <paramref name="sessionId"/>, if present.
    /// </summary>
    /// <param name="sessionId">The application-selected session id.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous delete operation.</returns>
    /// <exception cref="NotSupportedException">The underlying store does not support deletion.</exception>
    public ValueTask DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNullOrEmpty(sessionId);
        return this._sessionStore.DeleteSessionAsync(this.Agent, sessionId, cancellationToken);
    }

    /// <summary>
    /// Acquires an exclusive lock for <paramref name="sessionId"/> so concurrent requests for the same
    /// session serialize their get-run-save cycle. Dispose the returned value to release the lock.
    /// </summary>
    /// <param name="sessionId">The application-selected session id.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>An <see cref="IAsyncDisposable"/> that releases the lock when disposed.</returns>
    /// <remarks>
    /// When session locking is not enabled, this returns immediately with a no-op releaser.
    /// </remarks>
    public async ValueTask<IAsyncDisposable> LockSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNullOrEmpty(sessionId);

        if (this._sessionLocks is null)
        {
            return NoopReleaser.Instance;
        }

        SemaphoreSlim gate = this._sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreReleaser(gate);
    }

    private sealed class SemaphoreReleaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The semaphore is owned by the per-session dictionary and reused across callers; the releaser only releases it.")]
        private SemaphoreSlim? _gate = gate;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref this._gate, null)?.Release();
            return default;
        }
    }

    private sealed class NoopReleaser : IAsyncDisposable
    {
        public static readonly NoopReleaser Instance = new();

        public ValueTask DisposeAsync() => default;
    }
}
