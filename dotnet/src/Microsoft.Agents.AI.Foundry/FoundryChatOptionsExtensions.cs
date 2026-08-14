// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Foundry-specific extension methods for <see cref="ChatOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Use these helpers to attach per-call Foundry request fields:
/// <list type="bullet">
/// <item><description><see cref="WithHostedAgentSessionId"/> sends <c>agent_session_id</c> on the Responses body.</description></item>
/// <item><description><see cref="WithUserIdentity"/> sends <c>x-ms-user-identity</c> on the request.</description></item>
/// </list>
/// </para>
/// <para>
/// Hosted-agent session ids supplied via <see cref="WithHostedAgentSessionId"/> participate in the same
/// conflict rule as <see cref="ChatOptions.ConversationId"/>: if the <see cref="AgentSession"/> already
/// holds a different hosted id in its <see cref="AgentSession.StateBag"/>, the run throws
/// <see cref="System.InvalidOperationException"/>. Prefer pinning at session creation via
/// <see cref="FoundryAgent.CreateHostedSessionAsync(string?, string?, System.Threading.CancellationToken)"/>.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIOpenAIRequestPolicies)]
public static class FoundryChatOptionsExtensions
{
    /// <summary>HTTP header name for delegated application user identity.</summary>
    public const string UserIdentityHeaderName = "x-ms-user-identity";

    /// <summary>
    /// Well-known <see cref="ChatOptions.AdditionalProperties"/> key used to carry a per-call
    /// hosted-agent session id.
    /// </summary>
    internal const string HostedAgentSessionIdKey = "Microsoft.Agents.AI.Foundry.HostedAgentSessionId";

    /// <summary>
    /// Well-known <see cref="ChatOptions.AdditionalProperties"/> key used to carry the per-call
    /// user identity value.
    /// </summary>
    internal const string UserIdentityKey = "Microsoft.Agents.AI.Foundry.UserIdentity";

    /// <summary>
    /// Attaches a hosted-agent session id to the per-call <paramref name="options"/> carrier.
    /// </summary>
    /// <remarks>
    /// Only valid when the run's session has no hosted id yet, or already has this same id.
    /// Prefer <see cref="FoundryAgent.CreateHostedSessionAsync(string?, string?, System.Threading.CancellationToken)"/>
    /// to pin at session creation.
    /// </remarks>
    public static ChatOptions WithHostedAgentSessionId(this ChatOptions options, string hostedSessionId)
    {
        _ = Throw.IfNull(options);
        _ = Throw.IfNullOrWhitespace(hostedSessionId);

        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[HostedAgentSessionIdKey] = hostedSessionId;
        return options;
    }

    /// <summary>
    /// Attaches a delegated user identity value that will be sent as the
    /// <c>x-ms-user-identity</c> request header.
    /// </summary>
    /// <param name="options">The per-call chat options to mutate.</param>
    /// <param name="userIdentity">Opaque application user identifier. Must be non-empty.</param>
    /// <returns><paramref name="options"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// User identity is always request-scoped. It is never stored on <see cref="AgentSession"/>.
    /// </para>
    /// <para>
    /// Per Foundry hosted-agent isolation, a Responses chain created under one user cannot be
    /// continued by another user via <c>previous_response_id</c>, even when both calls share the
    /// same hosted sandbox (<c>agent_session_id</c>). See
    /// <see href="https://learn.microsoft.com/azure/foundry/agents/how-to/multiplex-session-users">Multiplex multiple users in one hosted agent session</see>.
    /// Reusing one <see cref="AgentSession"/> across identities typically reuses that chain, so the
    /// second identity's run fails at the platform (observed as a response not-found error). Prefer
    /// a distinct <see cref="AgentSession"/> per identity; those sessions may still share one hosted
    /// sandbox pin via <see cref="WithHostedAgentSessionId"/> or
    /// <see cref="FoundryAgent.CreateHostedSessionAsync(string?, string?, System.Threading.CancellationToken)"/>.
    /// </para>
    /// </remarks>
    public static ChatOptions WithUserIdentity(this ChatOptions options, string userIdentity)
    {
        _ = Throw.IfNull(options);
        _ = Throw.IfNullOrWhitespace(userIdentity);

        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[UserIdentityKey] = userIdentity;
        return options;
    }

    /// <summary>Reads the per-call hosted-agent session id stamped by <see cref="WithHostedAgentSessionId"/>.</summary>
    internal static string? GetHostedAgentSessionId(this ChatOptions options)
    {
        if (options.AdditionalProperties is null)
        {
            return null;
        }

        if (!options.AdditionalProperties.TryGetValue(HostedAgentSessionIdKey, out var raw))
        {
            return null;
        }

        return raw as string;
    }

    /// <summary>Reads the per-call user identity stamped by <see cref="WithUserIdentity"/>.</summary>
    internal static string? GetUserIdentity(this ChatOptions options)
    {
        if (options.AdditionalProperties is null)
        {
            return null;
        }

        if (!options.AdditionalProperties.TryGetValue(UserIdentityKey, out var raw))
        {
            return null;
        }

        return raw as string;
    }
}
