// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Default <see cref="IHostedTargetRunner"/> for <see cref="AIAgent"/> targets. Coerces the
/// <see cref="ChannelRequest.Input"/> into the agent's <c>RunAsync</c> message shape and wraps
/// the response in <see cref="HostedRunResult{TResult}"/>.
/// </summary>
public sealed class AIAgentRunner : IHostedTargetRunner
{
    private readonly AIAgent _agent;

    /// <summary>Initializes a new instance.</summary>
    public AIAgentRunner(AIAgent agent)
    {
        this._agent = Throw.IfNull(agent);
    }

    /// <inheritdoc />
    public async ValueTask<HostedRunResult> RunAsync(ChannelRequest request, CancellationToken cancellationToken)
    {
        Throw.IfNull(request);
        var messages = CoerceToMessages(request.Input);
        var response = await this._agent.RunAsync(messages, session: null, options: null, cancellationToken).ConfigureAwait(false);
        return new HostedRunResult<AgentResponse>
        {
            Result = response,
            Session = request.Session,
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<HostedStreamItem> StreamAsync(
        ChannelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Throw.IfNull(request);
        var messages = CoerceToMessages(request.Input);
        AgentResponseUpdate? final = null;
        await foreach (var update in this._agent.RunStreamingAsync(messages, session: null, options: null, cancellationToken).ConfigureAwait(false))
        {
            final = update;
            yield return new HostedStreamUpdate(update);
        }

        var aggregate = final is null
            ? new HostedRunResult<AgentResponseUpdate?> { Result = null, Session = request.Session }
            : (HostedRunResult)new HostedRunResult<AgentResponseUpdate> { Result = final, Session = request.Session };
        yield return new HostedStreamCompleted(aggregate);
    }

    private static ChatMessage[] CoerceToMessages(object input)
    {
        return input switch
        {
            string s => [new ChatMessage(ChatRole.User, s)],
            ChatMessage cm => [cm],
            IEnumerable<ChatMessage> seq => seq.ToArray(),
            _ => throw new ArgumentException(
                $"AIAgentRunner cannot coerce input of type '{input?.GetType().FullName ?? "<null>"}' into ChatMessage[]. " +
                "Channels should normalize input to string, ChatMessage, or IEnumerable<ChatMessage> before calling RunAsync.",
                nameof(input)),
        };
    }
}