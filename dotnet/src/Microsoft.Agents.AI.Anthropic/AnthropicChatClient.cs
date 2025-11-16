// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable CA1812

using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Client;
using Anthropic.Client.Models.Beta.Messages;
using Anthropic.Client.Models.Messages;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Anthropic;

/// <summary>
/// Provides a chat client implementation that integrates with Azure AI Agents, enabling chat interactions using
/// Azure-specific agent capabilities.
/// </summary>
internal sealed class AnthropicChatClient : IChatClient
{
    private readonly AnthropicClient _client;
    private readonly ChatClientMetadata _metadata;

    internal AnthropicChatClient(AnthropicClient client, Uri? endpoint = null, string? defaultModelId = null)
    {
        this._client = client;
        this._metadata = new ChatClientMetadata(providerName: "anthrendpointopic", providerUri: endpoint ?? new Uri("https://api.anthropic.com"), defaultModelId);
    }

    public void Dispose()
    {
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Message messageResponse = await this._client.Messages.Create(ChatClientHelper.CreateMessageParameters(this, messages, options));
        throw new NotImplementedException();
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return (serviceKey is null && serviceType == typeof(AnthropicClient))
            ? this._client
            : (serviceKey is null && serviceType == typeof(ChatClientMetadata))
            ? this._metadata
            : null;
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
