// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.AgentServer.Responses;
using Microsoft.Extensions.AI;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// A <see cref="ChatHistoryProvider"/> that reads the conversation history kept by the Foundry
/// platform for the current request, instead of keeping a copy inside the container.
/// </summary>
/// <remarks>
/// <para>
/// This is the provider a hosted agent gets when it was created without an explicit
/// <see cref="ChatClientAgentOptions.ChatHistoryProvider"/>. It makes the platform the single
/// source of the conversation: prior turns are read from the platform on every turn, and nothing
/// is written back, because the platform already persists the response items itself.
/// </para>
/// <para>
/// Reading goes through <see cref="ResponseContext.GetHistoryAsync"/>, which resolves the history
/// from <c>previous_response_id</c> and/or the <c>conversation</c> the request belongs to, returns
/// the items in chronological order, and caches the result for the request. An instance is created
/// per request because it holds that request's <see cref="ResponseContext"/>.
/// </para>
/// <para>
/// When an agent is created with its own chat history provider, that provider is used instead and
/// this one is never registered, so the agent's own store stays the single source.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
internal sealed class FoundryChatHistoryProvider : ChatHistoryProvider
{
    private readonly ResponseContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="FoundryChatHistoryProvider"/> class for a single request.
    /// </summary>
    /// <param name="context">The response context of the request being handled.</param>
    public FoundryChatHistoryProvider(ResponseContext context)
    {
        this._context = Throw.IfNull(context);
    }

    /// <inheritdoc />
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(context);

        var history = await this._context.GetHistoryAsync(cancellationToken).ConfigureAwait(false);

        return history.Count > 0
            ? InputConverter.ConvertOutputItemsToMessages(history, context.Session?.StateBag)
            : [];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Nothing is stored here, because the same turn is already persisted by the server that hosts
    /// this handler. When a request is made with <c>store</c> set to true, the response orchestrator
    /// hands the finished response to its responses provider, which writes the input items and the
    /// output items and links them to the conversation. Those are the very items a later turn reads
    /// back through <see cref="ResponseContext.GetHistoryAsync"/>. Writing them again from inside the
    /// container would keep a second copy of the same conversation in the agent session, which then
    /// diverges from the one the service serves.
    /// </para>
    /// <para>
    /// When <c>store</c> is false the service persists nothing, and there is also nothing for a later
    /// turn to read: history is resolved from <c>previous_response_id</c> or the conversation, both of
    /// which only exist for stored responses. Such a request is therefore self-contained, and storing
    /// its messages here would not make them reachable by any later turn either.
    /// </para>
    /// </remarks>
    protected override ValueTask StoreChatHistoryAsync(InvokedContext context, CancellationToken cancellationToken = default) => default;
}
