// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI;

/// <summary>
/// Defines the contract for storing and retrieving agent conversation sessions.
/// </summary>
/// <remarks>
/// Implementations enable persistent storage of conversation sessions, allowing conversations to be
/// resumed across HTTP requests, application restarts, or different service instances in hosted scenarios.
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
public abstract class AgentSessionStore
{
    /// <summary>
    /// Saves an agent session to persistent storage.
    /// </summary>
    /// <param name="agent">The agent that owns this session.</param>
    /// <param name="conversationId">The unique identifier for the conversation.</param>
    /// <param name="session">The session to save.</param>
    /// <param name="userId">
    /// The per-user partition key that scopes this session to its owner. Pass <see langword="null"/> only
    /// when there is no user context, such as in a single-user application or local development.
    /// Non-null values must not be empty or contain only whitespace. The parameter is required so every
    /// caller consciously decides the session scope.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    public abstract ValueTask SaveSessionAsync(
        AIAgent agent,
        string conversationId,
        AgentSession session,
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an agent session from persistent storage, or <see langword="null"/> when no session is stored
    /// for the given identifiers.
    /// </summary>
    /// <param name="agent">The agent that owns this session.</param>
    /// <param name="conversationId">The unique identifier for the conversation to retrieve.</param>
    /// <param name="userId">
    /// The per-user partition key that scopes this session to its owner. It must match the value used when the
    /// session was saved. Pass <see langword="null"/> only when there is no user context. Non-null values must
    /// not be empty or contain only whitespace.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result contains the restored session, or <see langword="null"/> when nothing is stored for
    /// the given identifiers. This method never creates a session.
    /// </returns>
    public abstract ValueTask<AgentSession?> GetSessionAsync(
        AIAgent agent,
        string conversationId,
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the stored session for the given identifiers, or creates a new one when none is stored.
    /// </summary>
    /// <param name="agent">The agent that owns this session.</param>
    /// <param name="conversationId">The unique identifier for the conversation to retrieve.</param>
    /// <param name="userId">The per-user partition key; see <see cref="GetSessionAsync"/> for its meaning.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task whose result is always a usable session.</returns>
    public virtual async ValueTask<AgentSession> GetOrCreateSessionAsync(
        AIAgent agent,
        string conversationId,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(agent);

        return await this.GetSessionAsync(agent, conversationId, userId, cancellationToken).ConfigureAwait(false)
            ?? await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
    }
}
