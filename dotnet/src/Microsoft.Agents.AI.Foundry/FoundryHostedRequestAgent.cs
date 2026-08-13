// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001
#pragma warning disable SCME0001
#pragma warning disable MEAI001

namespace Microsoft.Agents.AI.Foundry;

/// <summary>
/// Delegating agent that applies Foundry hosted-agent request context per run:
/// resolves the sticky hosted-agent session id, injects <c>agent_session_id</c> into the
/// Responses body, stamps <c>x-ms-user-identity</c>, and writes the platform-returned session
/// id back onto the <see cref="AgentSession"/>.
/// </summary>
internal sealed class FoundryHostedRequestAgent : DelegatingAIAgent
{
    public FoundryHostedRequestAgent(AIAgent innerAgent)
        : base(innerAgent)
    {
    }

    /// <inheritdoc/>
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prepared = Prepare(session, options);
        var response = await this.InnerAgent.RunAsync(messages, session, prepared.Options, cancellationToken).ConfigureAwait(false);
        ApplySessionSticky(session, prepared.SessionIdBox);
        return response;
    }

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prepared = Prepare(session, options);

        await foreach (var update in this.InnerAgent.RunStreamingAsync(messages, session, prepared.Options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }

        ApplySessionSticky(session, prepared.SessionIdBox);
    }

    private static PreparedRun Prepare(AgentSession? session, AgentRunOptions? options)
    {
        ChatOptions? chatOptions = options is ChatClientAgentRunOptions cro ? cro.ChatOptions : null;

        string? sessionHostedId = session?.GetHostedAgentSessionId();
        string? optionsHostedId = chatOptions?.GetHostedAgentSessionId();

        if (!string.IsNullOrWhiteSpace(sessionHostedId)
            && !string.IsNullOrWhiteSpace(optionsHostedId)
            && !string.Equals(sessionHostedId, optionsHostedId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                """
                The hosted-agent session id provided via ChatOptions is different from the id stored on the provided AgentSession.
                Only one hosted-agent session id can be used for a run.
                """);
        }

        string? resolvedHostedId = !string.IsNullOrWhiteSpace(optionsHostedId) ? optionsHostedId : sessionHostedId;
        var sessionIdBox = new StrongBox<string?>(resolvedHostedId);
        HostedSessionIdCaptureScope.Current = sessionIdBox;

        // Always ensure ChatOptions + factory so (a) an existing id is sent on every service call
        // and (b) a platform-created id captured mid-run is sent on later function-loop calls.
        var effectiveOptions = EnsureChatOptions(options, out chatOptions);
        AttachHostedSessionIdFactory(chatOptions, sessionIdBox);

        string? userIdentity = chatOptions.GetUserIdentity();
        if (!string.IsNullOrWhiteSpace(userIdentity))
        {
            UserIdentityScope.Current = userIdentity;
        }

        return new PreparedRun(effectiveOptions, sessionIdBox);
    }

    private static ChatClientAgentRunOptions EnsureChatOptions(AgentRunOptions? options, out ChatOptions chatOptions)
    {
        if (options is ChatClientAgentRunOptions existing)
        {
            existing.ChatOptions ??= new ChatOptions();
            chatOptions = existing.ChatOptions;
            return existing;
        }

        chatOptions = new ChatOptions();
        return new ChatClientAgentRunOptions(chatOptions);
    }

    private static void AttachHostedSessionIdFactory(ChatOptions chatOptions, StrongBox<string?> sessionIdBox)
    {
        var previousFactory = chatOptions.RawRepresentationFactory;
        chatOptions.RawRepresentationFactory = client =>
        {
            object? previous = previousFactory?.Invoke(client);
            if (previous is not null and not CreateResponseOptions)
            {
                return previous;
            }

            var responseOptions = previous as CreateResponseOptions ?? new CreateResponseOptions();
            if (!string.IsNullOrWhiteSpace(sessionIdBox.Value))
            {
                responseOptions.Patch.Set("$.agent_session_id"u8, sessionIdBox.Value);
            }

            return responseOptions;
        };
    }

    private static void ApplySessionSticky(AgentSession? session, StrongBox<string?> sessionIdBox)
    {
        if (session is null || string.IsNullOrWhiteSpace(sessionIdBox.Value))
        {
            return;
        }

        session.SetHostedAgentSessionId(sessionIdBox.Value!);
    }

    private sealed class PreparedRun
    {
        public PreparedRun(ChatClientAgentRunOptions options, StrongBox<string?> sessionIdBox)
        {
            this.Options = options;
            this.SessionIdBox = sessionIdBox;
        }

        public ChatClientAgentRunOptions Options { get; }
        public StrongBox<string?> SessionIdBox { get; }
    }
}
