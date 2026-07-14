// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Agents.AI;

/// <summary>
/// A delegating chat client that strengthens the human-in-the-loop tool-approval control by binding each inbound
/// <see cref="ToolApprovalResponseContent"/> to the model-originated <see cref="ToolApprovalRequestContent"/> that
/// the framework actually surfaced, so an approved tool call always matches what a human was asked to approve.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FunctionInvokingChatClient"/> (FICC) executes the <see cref="ToolApprovalResponseContent.ToolCall"/>
/// carried by an approval response. This decorator adds an extra layer of assurance above FICC: it guarantees that
/// only approvals the framework actually requested are honored, and that an approved call runs with exactly the tool
/// name and arguments that were surfaced for approval.
/// </para>
/// <para>
/// This decorator sits above <see cref="FunctionInvokingChatClient"/> in the pipeline. On outbound responses it
/// records every model-originated <see cref="ToolApprovalRequestContent"/> that FICC surfaced into the session's
/// <see cref="AgentSessionStateBag"/>, keyed by request id. On inbound requests it processes each
/// <see cref="ToolApprovalResponseContent"/> before it reaches FICC:
/// <list type="bullet">
/// <item>If a recorded pending request exists for the response's request id, the response's tool call is rebound to
/// the recorded (model-originated) tool call, so the approved call always matches the surfaced request's tool name
/// and arguments. The pending entry is then consumed so an approval is honored only once.</item>
/// <item>If no recorded pending request exists, the response (and any unrecorded approval request in the same
/// messages) is ignored, so only approvals tied to a genuine, framework-issued request take effect.</item>
/// </list>
/// </para>
/// <para>
/// This decorator operates within the context of a running <see cref="AIAgent"/> with an active
/// <see cref="AgentRunContext.Session"/>. When invoked without an ambient run context or session (for example when
/// the chat client is used directly outside of an agent run), the decorator becomes a no-op: it passes the request
/// through unchanged and logs a warning, because there is no framework-tracked pending state to validate against.
/// </para>
/// </remarks>
internal sealed partial class ApprovalResponseBindingChatClient : DelegatingChatClient
{
    /// <summary>
    /// The key used in <see cref="AgentSessionStateBag"/> to store the model-originated pending approval requests
    /// between agent runs.
    /// </summary>
    internal const string StateBagKey = "_pendingApprovalRequests";

    private readonly ILogger _logger;

    private bool _warnedNoSession;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApprovalResponseBindingChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The underlying chat client (typically the pipeline containing <see cref="FunctionInvokingChatClient"/>).</param>
    /// <param name="loggerFactory">An optional <see cref="ILoggerFactory"/> used to create a logger for diagnostics.</param>
    public ApprovalResponseBindingChatClient(IChatClient innerClient, ILoggerFactory? loggerFactory = null)
        : base(innerClient)
    {
        this._logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ApprovalResponseBindingChatClient>();
    }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.TryGetSession(out var session))
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }

        messages = this.ValidateInboundApprovalResponses(messages, session);

        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        this.RecordPendingApprovalRequests(response.Messages, session);

        return response;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!this.TryGetSession(out var session))
        {
            await foreach (var passthrough in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                yield return passthrough;
            }

            yield break;
        }

        messages = this.ValidateInboundApprovalResponses(messages, session);

        List<ToolApprovalRequestContent>? emitted = null;

        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                foreach (var content in update.Contents)
                {
                    if (content is ToolApprovalRequestContent request)
                    {
                        (emitted ??= []).Add(request);
                    }
                }

                yield return update;
            }
        }
        finally
        {
            if (emitted is { Count: > 0 })
            {
                this.MergePendingApprovalRequests(emitted, session);
            }
        }
    }

    /// <summary>
    /// Attempts to get the current <see cref="AgentSession"/> from the ambient run context. When no run
    /// context or session is available, logs a warning (once per instance) and returns <see langword="false"/>
    /// so the caller can pass the request through without applying validation.
    /// </summary>
    private bool TryGetSession([NotNullWhen(true)] out AgentSession? session)
    {
        session = AIAgent.CurrentRunContext?.Session;

        if (session is null)
        {
            if (!this._warnedNoSession)
            {
                this._warnedNoSession = true;
                LogValidationSkipped(this._logger);
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Rewrites the inbound messages so that only <see cref="ToolApprovalResponseContent"/> items bound to a
    /// recorded, model-originated <see cref="ToolApprovalRequestContent"/> survive, with their tool call rebound to
    /// the recorded call. Responses and approval requests that do not correspond to a recorded pending request are
    /// removed. Consumed pending entries are dropped from the session state to prevent replay.
    /// </summary>
    private IEnumerable<ChatMessage> ValidateInboundApprovalResponses(IEnumerable<ChatMessage> messages, AgentSession session)
    {
        var messageList = messages as IList<ChatMessage> ?? new List<ChatMessage>(messages);

        // Quick check: is there any approval request/response content worth validating?
        if (!ContainsApprovalContent(messageList))
        {
            return messages;
        }

        // Load the model-originated pending requests recorded on previous outbound turns.
        var pending = LoadPendingApprovalRequests(session);
        var byRequestId = new Dictionary<string, ToolApprovalRequestContent>(StringComparer.Ordinal);
        foreach (var request in pending)
        {
            byRequestId[request.RequestId] = request;
        }

        var result = new List<ChatMessage>(messageList.Count);
        HashSet<string>? consumedRequestIds = null;
        bool anyModified = false;

        foreach (var message in messageList)
        {
            if (!ContainsApprovalContent(message))
            {
                result.Add(message);
                continue;
            }

            var newContents = new List<AIContent>(message.Contents.Count);
            bool messageModified = false;
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case ToolApprovalResponseContent response:
                        messageModified = true;
                        if (byRequestId.TryGetValue(response.RequestId, out var matchedRequest))
                        {
                            // Rebind the tool call to the model-originated call so the approved call matches the
                            // tool name and arguments that were surfaced for approval.
                            newContents.Add(new ToolApprovalResponseContent(response.RequestId, response.Approved, matchedRequest.ToolCall)
                            {
                                Reason = response.Reason,
                            });
                            (consumedRequestIds ??= new(StringComparer.Ordinal)).Add(response.RequestId);
                        }
                        else
                        {
                            // Only approvals tied to a request the framework surfaced are honored; ignore this one.
                            LogIgnoredUnboundResponse(this._logger, response.RequestId);
                        }

                        break;

                    case ToolApprovalRequestContent request when !byRequestId.ContainsKey(request.RequestId):
                        // Keep only approval requests the framework issued so responses bind to a known request.
                        messageModified = true;
                        LogIgnoredUnboundRequest(this._logger, request.RequestId);
                        break;

                    default:
                        newContents.Add(content);
                        break;
                }
            }

            if (!messageModified)
            {
                result.Add(message);
                continue;
            }

            anyModified = true;

            if (newContents.Count > 0)
            {
                var cloned = message.Clone();
                cloned.Contents = newContents;
                result.Add(cloned);
            }
        }

        // Consume matched pending entries so an approval cannot be replayed on a later turn.
        if (consumedRequestIds is { Count: > 0 })
        {
            pending.RemoveAll(r => consumedRequestIds.Contains(r.RequestId));
            SavePendingApprovalRequests(pending, session);
        }

        return anyModified ? result : messages;
    }

    /// <summary>
    /// Records model-originated <see cref="ToolApprovalRequestContent"/> items found in the response messages into
    /// the session so they can be matched against the caller's approval responses on the next request.
    /// </summary>
    private void RecordPendingApprovalRequests(IList<ChatMessage> messages, AgentSession session)
    {
        List<ToolApprovalRequestContent>? emitted = null;

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is ToolApprovalRequestContent request)
                {
                    (emitted ??= []).Add(request);
                }
            }
        }

        if (emitted is { Count: > 0 })
        {
            this.MergePendingApprovalRequests(emitted, session);
        }
    }

    /// <summary>
    /// Merges newly surfaced approval requests into the recorded pending set, de-duplicating by request id.
    /// </summary>
    private void MergePendingApprovalRequests(List<ToolApprovalRequestContent> emitted, AgentSession session)
    {
        var pending = LoadPendingApprovalRequests(session);

        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in pending)
        {
            known.Add(request.RequestId);
        }

        bool changed = false;
        foreach (var request in emitted)
        {
            if (known.Add(request.RequestId))
            {
                pending.Add(request);
                changed = true;
            }
        }

        if (changed)
        {
            SavePendingApprovalRequests(pending, session);
        }
    }

    private static List<ToolApprovalRequestContent> LoadPendingApprovalRequests(AgentSession session)
        => session.StateBag.TryGetValue<List<ToolApprovalRequestContent>>(StateBagKey, out var pending, AgentJsonUtilities.DefaultOptions)
            && pending is not null
            ? pending
            : [];

    private static void SavePendingApprovalRequests(List<ToolApprovalRequestContent> pending, AgentSession session)
    {
        if (pending.Count > 0)
        {
            session.StateBag.SetValue(StateBagKey, pending, AgentJsonUtilities.DefaultOptions);
        }
        else
        {
            session.StateBag.TryRemoveValue(StateBagKey);
        }
    }

    private static bool ContainsApprovalContent(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            if (ContainsApprovalContent(message))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsApprovalContent(ChatMessage message)
    {
        foreach (var content in message.Contents)
        {
            if (content is ToolApprovalResponseContent or ToolApprovalRequestContent)
            {
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(LogLevel.Warning, "ApprovalResponseBindingChatClient was invoked without an active agent run context or session. Approval-response binding is skipped. Invoke the chat client through AIAgent.RunAsync or AIAgent.RunStreamingAsync to enable binding.")]
    private static partial void LogValidationSkipped(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "Ignored a ToolApprovalResponseContent with request id '{RequestId}' that does not correspond to a model-originated approval request surfaced by the framework.")]
    private static partial void LogIgnoredUnboundResponse(ILogger logger, string requestId);

    [LoggerMessage(LogLevel.Warning, "Ignored a ToolApprovalRequestContent with request id '{RequestId}' that the framework did not surface.")]
    private static partial void LogIgnoredUnboundRequest(ILogger logger, string requestId);
}
