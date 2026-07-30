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
    /// Nothing is stored here. The platform persists the items of a response as part of serving the
    /// request, so writing them again from inside the container would keep a second, diverging copy
    /// of the same conversation.
    /// </remarks>
    protected override ValueTask StoreChatHistoryAsync(InvokedContext context, CancellationToken cancellationToken = default) => default;
}
