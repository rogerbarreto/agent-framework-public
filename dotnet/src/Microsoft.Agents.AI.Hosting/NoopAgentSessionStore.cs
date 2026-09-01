// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI.Hosting;

/// <summary>
/// This store implementation does not have any store under the hood and therefore does not store sessions.
/// <see cref="GetSessionAsync(AIAgent, string, string?, CancellationToken)"/> always returns <see langword="null"/>.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
public sealed class NoopAgentSessionStore : AgentSessionStore
{
    /// <inheritdoc/>
    public override ValueTask SaveSessionAsync(
        AIAgent agent,
        string conversationId,
        AgentSession session,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        return default;
    }

    /// <inheritdoc/>
    public override ValueTask<AgentSession?> GetSessionAsync(
        AIAgent agent,
        string conversationId,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        return new((AgentSession?)null);
    }
}
