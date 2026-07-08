// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Hosting.OpenAI;

/// <summary>
/// The result of converting an OpenAI Responses request body into Agent Framework run values via
/// <see cref="OpenAIResponses.ToAgentRunRequest(System.Text.Json.JsonElement, OpenAIResponsesMapOptions?)"/>.
/// </summary>
/// <remarks>
/// This type carries the values an application passes to <see cref="AIAgent.RunAsync(IEnumerable{ChatMessage}, AgentSession?, AgentRunOptions?, System.Threading.CancellationToken)"/>
/// (or the streaming equivalent) when it owns its own hosting route. It does not run the agent; the
/// application remains in control of when and how the run happens.
/// </remarks>
public sealed class OpenAIResponsesRunRequest
{
    internal OpenAIResponsesRunRequest(IList<ChatMessage> messages, AgentRunOptions? options)
    {
        this.Messages = messages;
        this.Options = options;
    }

    /// <summary>
    /// Gets the chat messages parsed from the request body, ready to pass to an <see cref="AIAgent"/> run.
    /// </summary>
    public IList<ChatMessage> Messages { get; }

    /// <summary>
    /// Gets the run options mapped from the request, or <see langword="null"/> when no request setting is
    /// mapped onto the run. The mapping is controlled by <see cref="OpenAIResponsesMapOptions.RunOptionsFactory"/>;
    /// by default no request setting is mapped.
    /// </summary>
    public AgentRunOptions? Options { get; }
}
