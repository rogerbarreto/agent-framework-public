// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses;

/// <summary>
/// Executes a response after the endpoint-specific executor has selected an agent.
/// </summary>
internal static class AgentResponseExecution
{
    private const string SessionInitializedStateKey = "Microsoft.Agents.AI.Hosting.OpenAI.Responses.Initialized";

    /// <summary>
    /// Validates that requests which resume an approval have server-side session storage available.
    /// </summary>
    public static ResponseError? ValidateSessionRequirements(CreateResponse request, bool hasSessionStore)
    {
        if (hasSessionStore)
        {
            return null;
        }

        foreach (InputMessage inputMessage in request.Input.GetInputMessages())
        {
            if (inputMessage.Content.Contents is not { } contents)
            {
                continue;
            }

            foreach (ItemContent content in contents)
            {
                if (content is ItemContentFunctionApprovalResponse)
                {
                    // Approval responses are trusted only when matched to a request recorded by the server.
                    // Without session storage, continuing would silently ignore the decision or trust caller data.
                    return new ResponseError
                    {
                        Code = ResponseErrorCodes.InvalidRequest,
                        Message = "Approval-required function calling is not supported because no AgentSessionStore is configured."
                    };
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the selected agent and converts its updates to Responses API events.
    /// </summary>
    public static async IAsyncEnumerable<StreamingResponseEvent> ExecuteAsync(
        AIAgent agent,
        OpenAIResponsesMapOptions mapOptions,
        AgentInvocationContext context,
        CreateResponse request,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The hosting developer controls, via OpenAIResponsesMapOptions.RunOptionsFactory, which request
        // settings are mapped onto the agent run. By default no request setting is mapped.
        AgentRunOptions? options = mapOptions.RunOptionsFactory(request.ToRequestInfo());

        // A response ID identifies an immutable continuation snapshot. A conversation ID identifies the
        // mutable conversation head. A new response without either starts under its generated response ID.
        AIHostAgent? hostAgent = agent as AIHostAgent;
        AgentSession? session = null;
        bool includeConversationHistory = true;
        if (hostAgent is not null)
        {
            string sessionId = request.Conversation?.Id
                ?? request.PreviousResponseId
                ?? context.ResponseId;
            session = await hostAgent.GetOrCreateSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);

            // A new agent session must be seeded from an existing conversation transcript. Once initialized,
            // the session owns both history and pending approvals, so replaying the transcript would duplicate
            // messages and could resubmit approval content that was already processed.
            includeConversationHistory = !session.StateBag.TryGetValue<string>(SessionInitializedStateKey, out _);
            session.StateBag.SetValue(SessionInitializedStateKey, bool.TrueString);
        }

        // Convert input to chat messages, prepending conversation history only when it is not already
        // represented by a restored agent session.
        var messages = new List<ChatMessage>();
        if (includeConversationHistory && conversationHistory is not null)
        {
            messages.AddRange(conversationHistory);
        }

        foreach (InputMessage inputMessage in request.Input.GetInputMessages())
        {
            messages.Add(inputMessage.ToChatMessage());
        }

        // Convert streaming agent updates to Responses API events. For persisted sessions, hold the
        // terminal event until the state has been saved. Otherwise a streaming client can immediately
        // continue the response before the approval checkpoint is available.
        StreamingResponseCompleted? completedEvent = null;
        await foreach (StreamingResponseEvent streamingEvent in agent.RunStreamingAsync(messages, session, options, cancellationToken)
            .ToStreamingResponseAsync(request, context, cancellationToken)
            .ConfigureAwait(false))
        {
            if (hostAgent is not null && streamingEvent is StreamingResponseCompleted completed)
            {
                completedEvent = completed;
                continue;
            }

            yield return streamingEvent;
        }

        if (hostAgent is not null && session is not null)
        {
            // Response IDs are immutable snapshots and honor store=false. A conversation ID is a mutable
            // head and always advances after a successful turn.
            if (request.Store is not false)
            {
                await hostAgent.SaveSessionAsync(context.ResponseId, session, cancellationToken).ConfigureAwait(false);
            }

            if (request.Conversation?.Id is { } conversationId)
            {
                await hostAgent.SaveSessionAsync(conversationId, session, cancellationToken).ConfigureAwait(false);
            }
        }

        // Publish completion only after persistence so an immediate continuation can restore the session.
        if (completedEvent is not null)
        {
            yield return completedEvent;
        }
    }
}
