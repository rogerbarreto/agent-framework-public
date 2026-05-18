// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Foundry;

/// <summary>
/// Foundry-specific extensions on <see cref="FoundryAgent"/>. Mirrors Python's free
/// <c>to_prompt_agent(agent)</c> function.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
public static class FoundryAgentExtensions
{
    /// <summary>
    /// Converts the supplied <see cref="FoundryAgent"/> into a <see cref="ProjectsAgentDefinition"/>
    /// ready to publish via <c>AgentAdministrationClient.CreateAgentVersionAsync</c>.
    /// </summary>
    /// <remarks>
    /// The agent-endpoint construction mode is not convertible because no local definition exists;
    /// conversion in that case throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    /// <param name="agent">The Foundry agent to convert.</param>
    /// <param name="cancellationToken">A token that can cancel an internal server-side fetch when the agent was constructed from a bare <see cref="AgentReference"/>.</param>
    /// <returns>A <see cref="ProjectsAgentDefinition"/> suitable for publishing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The agent's chat client is not a <see cref="FoundryChatClient"/>; the agent was constructed from a hosted agent endpoint URL; no model id is set on the agent's <see cref="ChatOptions"/> for the responses-API mode; or the agent contains an <see cref="AITool"/> that cannot be converted to a <c>ResponseTool</c>.</exception>
    public static Task<ProjectsAgentDefinition> ToPromptAgentAsync(this FoundryAgent agent, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agent);

        var innerChatClient = agent.GetService<IChatClient>()
            ?? throw new InvalidOperationException(
                "ToPromptAgentAsync could not resolve the inner IChatClient on the FoundryAgent.");
        var chatOptions = agent.GetService<ChatOptions>();
        return FoundryPromptAgentConverter.ConvertAsync(innerChatClient, chatOptions, cancellationToken);
    }
}
