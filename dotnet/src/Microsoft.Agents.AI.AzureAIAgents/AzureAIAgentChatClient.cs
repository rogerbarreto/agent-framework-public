// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.AzureAIAgents;

/// <summary>
/// Provides a chat client implementation that integrates with Azure AI Agents, enabling chat interactions using
/// Azure-specific agent capabilities.
/// </summary>
internal sealed class AzureAIAgentChatClient : DelegatingChatClient
{
    private readonly ChatClientMetadata? _metadata;

    internal AzureAIAgentChatClient(IChatClient innerClient) : base(innerClient)
    {
        this._metadata = new ChatClientMetadata("azure.ai.agents");
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        return (serviceKey is null && serviceType == typeof(ChatClientMetadata))
            ? this._metadata
            : base.GetService(serviceType, serviceKey);
    }
}
