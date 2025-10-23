// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using Azure.AI.Agents;
using Microsoft.Extensions.AI;

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Microsoft.Agents.AI.AzureAIAgents;

/// <summary>
/// Provides an agent implementation that integrates with Azure AI Agents services, enabling chat-based interactions.
/// </summary>
internal sealed class AzureAIAgent : DelegatingAIAgent
{
    private readonly AgentsClient _agentsClient;
    private readonly AgentVersion _agentVersion;
    private readonly AIAgentMetadata _agentMetadata;

    internal AzureAIAgent(AgentsClient agentsClient, AgentVersion agentVersion, AIAgent innerAgent) : base(innerAgent)
    {
        this._agentsClient = agentsClient;
        this._agentVersion = agentVersion;
        this._agentMetadata = new AIAgentMetadata("azure.ai.agents");
    }

    /// <inheritdoc />
    public async override Task<AgentRunResponse> RunAsync(IEnumerable<ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        this.ValidateChatClient();
        this.ValidateThread(thread);

        return await this.InnerAgent.RunAsync(messages, thread, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async override IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(IEnumerable<ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        this.ValidateChatClient();
        this.ValidateThread(thread);

        await foreach (var update in this.InnerAgent.RunStreamingAsync(messages, thread, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        return (serviceKey is null && serviceType == typeof(AgentVersion))
            ? this._agentVersion
            : (serviceKey is null && serviceType == typeof(AgentsClient))
            ? this._agentsClient
            : (serviceKey is null && serviceType == typeof(AIAgentMetadata))
            ? this._agentMetadata
            : base.GetService(serviceType, serviceKey);
    }

    private void ValidateChatClient()
    {
        _ = this.GetService<AzureAIAgentChatClient>()
            ?? throw new InvalidOperationException("The provided IChatClient needs to be decorated with a AzureAIAgent ChatClient for the agent to function properly.");
    }

    private void ValidateThread(AgentThread? thread)
    {
        if (thread is null)
        {
            return;
        }

        if (thread is not ChatClientAgentThread asChatClientAgentThread)
        {
            throw new InvalidOperationException("The provided thread is not compatible with the agent. Only threads created by the agent can be used.");
        }

        if (string.IsNullOrWhiteSpace(asChatClientAgentThread.ConversationId))
        {
            throw new InvalidOperationException("The ChatClientAgentThread does not have a valid ConversationId.");
        }
    }
}
