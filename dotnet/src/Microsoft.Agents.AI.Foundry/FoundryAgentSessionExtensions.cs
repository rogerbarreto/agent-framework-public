// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI;

/// <summary>
/// Foundry-specific extension methods for <see cref="AgentSession"/>.
/// </summary>
/// <remarks>
/// <para>
/// The hosted-agent session id (sandbox / <c>agent_session_id</c>) is stored in
/// <see cref="AgentSession.StateBag"/> under <see cref="HostedAgentSessionIdKey"/>. That keeps
/// Foundry-specific state off the sealed <see cref="ChatClientAgentSession"/> type while still
/// serializing with the session.
/// </para>
/// <para>
/// This is not <see cref="Extensions.AI.ChatOptions.AdditionalProperties"/>. Per-call
/// overrides use
/// <see cref="Extensions.AI.FoundryChatOptionsExtensions.WithHostedAgentSessionId(Extensions.AI.ChatOptions, string)"/>.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIOpenAIRequestPolicies)]
public static class FoundryAgentSessionExtensions
{
    /// <summary>
    /// Well-known <see cref="AgentSessionStateBag"/> key for the sticky hosted-agent session id.
    /// </summary>
    public const string HostedAgentSessionIdKey = "Microsoft.Agents.AI.Foundry.HostedAgentSessionId";

    /// <summary>
    /// Gets the sticky hosted-agent session id from <paramref name="session"/>, or
    /// <see langword="null"/> if none is stored.
    /// </summary>
    /// <remarks>
    /// Prefer creating/pinning via
    /// <see cref="FoundryAgent.CreateHostedSessionAsync(string?, string?, System.Threading.CancellationToken)"/>.
    /// This getter is for reading the id after the platform assigns one (or after an explicit pin).
    /// </remarks>
    public static string? GetHostedAgentSessionId(this AgentSession session)
    {
        _ = Throw.IfNull(session);
        return session.StateBag.TryGetValue<string>(HostedAgentSessionIdKey, out var value)
            ? value
            : null;
    }

    /// <summary>Sets the sticky hosted-agent session id on <paramref name="session"/>.</summary>
    internal static void SetHostedAgentSessionId(this AgentSession session, string hostedSessionId)
    {
        _ = Throw.IfNull(session);
        _ = Throw.IfNullOrWhitespace(hostedSessionId);
        session.StateBag.SetValue(HostedAgentSessionIdKey, hostedSessionId);
    }
}
