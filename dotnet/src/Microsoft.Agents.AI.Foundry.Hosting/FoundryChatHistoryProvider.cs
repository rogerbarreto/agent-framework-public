// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.AgentServer.Responses;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// A <see cref="ChatHistoryProvider"/> that reads the conversation the Foundry service keeps for the
/// current request, and keeps in the agent session only the turns the service will not keep itself.
/// </summary>
/// <remarks>
/// <para>
/// This is the provider a hosted agent gets when it was created without an explicit
/// <see cref="ChatClientAgentOptions.ChatHistoryProvider"/>. Reading goes through
/// <see cref="ResponseContext.GetHistoryAsync"/>, which resolves the history from
/// <c>previous_response_id</c> and/or the <c>conversation</c> the request belongs to, returns the
/// items in chronological order, and caches the result for the request. An instance is created per
/// request because it holds that request's <see cref="ResponseContext"/>.
/// </para>
/// <para>
/// Writing depends on whether the request is stored, so that every turn is kept exactly once:
/// </para>
/// <list type="bullet">
/// <item><description>
/// With <c>store</c> true the service persists this turn itself: the response orchestrator hands the
/// finished response to its responses provider, which writes the input and output items and links
/// them to the conversation. Those are the very items a later turn reads back. Writing them into the
/// agent session as well would keep a second copy that then diverges from the one the service serves,
/// so nothing is written here.
/// </description></item>
/// <item><description>
/// With <c>store</c> false the service persists nothing, and a later turn asking for this
/// conversation gets nothing back for it. The turn is therefore kept in the agent session, which is
/// the only memory it can have. Earlier stored turns of the same conversation are still read from the
/// service, so a conversation that mixes stored and unstored turns stays whole.
/// </description></item>
/// </list>
/// <para>
/// Once a conversation holds turns the service never saw, a stored turn is refused: the service would
/// record it on top of turns it does not have, leaving a gap for anyone reading the conversation back
/// from it. The refusal happens before the model is called.
/// </para>
/// <para>
/// What is kept belongs to the <see cref="AgentSession"/>, not to this object: a new provider is built
/// for every request, and the turns it keeps are written into the session's state bag under this
/// provider's own state key. Two sessions following the same service-side conversation therefore keep
/// their unstored turns apart, and each still reads the stored ones from the service.
/// </para>
/// <para>
/// When an agent is created with its own chat history provider, that provider is used instead and
/// this one is never registered, so the agent's own store stays the single source.
/// </para>
/// </remarks>
internal sealed class FoundryChatHistoryProvider : ChatHistoryProvider
{
    private readonly ResponseContext _context;
    private readonly bool _serviceStoresThisTurn;
    private readonly ProviderSessionState<State> _sessionState;
    private IReadOnlyList<string>? _stateKeys;

    /// <summary>
    /// Initializes a new instance of the <see cref="FoundryChatHistoryProvider"/> class for a single request.
    /// </summary>
    /// <param name="context">The response context of the request being handled.</param>
    /// <param name="serviceStoresThisTurn">
    /// <see langword="true"/> when the request was made with <c>store</c> enabled, so the service keeps
    /// this turn; <see langword="false"/> when it keeps nothing and the turn must be kept in the session.
    /// </param>
    public FoundryChatHistoryProvider(ResponseContext context, bool serviceStoresThisTurn)
    {
        this._context = Throw.IfNull(context);
        this._serviceStoresThisTurn = serviceStoresThisTurn;
        this._sessionState = new ProviderSessionState<State>(_ => new State(), nameof(FoundryChatHistoryProvider));
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> StateKeys => this._stateKeys ??= [this._sessionState.StateKey];

    /// <inheritdoc />
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(context);

        var unstored = this._sessionState.GetOrInitializeState(context.Session).Messages;

        // Once a conversation has turns the service never saw, it cannot go back to being stored by the
        // service: the service would record this turn on top of turns it does not have, so anyone
        // reading the conversation back from it would get an answer with no question. Refuse up front
        // rather than let that gap be written.
        if (this._serviceStoresThisTurn && unstored.Count > 0)
        {
            throw new InvalidOperationException(
                """
                This conversation has turns that were not stored by the service, so a stored turn cannot be added to it.
                The service would record this turn without the turns that came before it, leaving a gap in the stored conversation.
                Either keep using store=false for this conversation, or start a new one for stored turns.
                """);
        }

        var served = await this._context.GetHistoryAsync(cancellationToken).ConfigureAwait(false);

        // The service's turns come first: they are the earlier part of the conversation, and anything
        // kept in the session is by definition a turn the service did not record, which happened after.
        IEnumerable<ChatMessage> history = served.Count > 0
            ? InputConverter.ConvertOutputItemsToMessages(served, context.Session?.StateBag)
            : [];

        return unstored.Count > 0 ? history.Concat(unstored) : history;
    }

    /// <inheritdoc />
    protected override ValueTask StoreChatHistoryAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(context);

        if (this._serviceStoresThisTurn)
        {
            return default;
        }

        // Only the messages of this turn arrive here: the base class filters out everything marked as
        // chat history, which is what was handed back by ProvideChatHistoryAsync above.
        var state = this._sessionState.GetOrInitializeState(context.Session);
        state.Messages.AddRange(context.RequestMessages);
        if (context.ResponseMessages is not null)
        {
            state.Messages.AddRange(context.ResponseMessages);
        }

        this._sessionState.SaveState(context.Session, state);
        return default;
    }

    /// <summary>
    /// The turns of a conversation that the service was not asked to store, held in the
    /// <see cref="AgentSession.StateBag"/> so they survive with the session.
    /// </summary>
    public sealed class State
    {
        /// <summary>Gets or sets the messages of the turns the service did not store.</summary>
        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = [];
    }
}
