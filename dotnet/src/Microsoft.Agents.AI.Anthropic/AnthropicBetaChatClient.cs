// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable CA1812

using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Anthropic;

/// <summary>
/// Provides a chat client implementation that integrates with Azure AI Agents, enabling chat interactions using
/// Azure-specific agent capabilities.
/// </summary>
internal sealed class AnthropicBetaChatClient : IChatClient
{
    private readonly AnthropicClient _client;
    private readonly ChatClientMetadata _metadata;

    internal AnthropicBetaChatClient(AnthropicClient client, Uri? endpoint = null, string? defaultModelId = null)
    {
        this._client = client;
        this._metadata = new ChatClientMetadata(providerName: "anthropic", providerUri: endpoint ?? new Uri("https://api.anthropic.com"), defaultModelId);
    }

    public void Dispose()
    {
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var modelId = options?.ModelId ?? this._metadata.DefaultModelId
            ?? throw new InvalidOperationException("No model ID specified in options or default model provided at the client initialization.");

        BetaMessage response = await this._client.Beta.Messages.Create(ChatClientHelper.CreateBetaMessageParameters(this, modelId, messages, options), cancellationToken).ConfigureAwait(false);

        ChatMessage chatMessage = new(ChatRole.Assistant, ChatClientHelper.ProcessResponseContent(response));

        return new ChatResponse(chatMessage)
        {
            ResponseId = response.ID,
            FinishReason = response.StopReason?.Value() switch
            {
                BetaStopReason.MaxTokens => ChatFinishReason.Length,
                _ => ChatFinishReason.Stop,
            },
            ModelId = response.Model,
            RawRepresentation = response,
            Usage = response.Usage is { } usage ? ChatClientHelper.CreateUsageDetails(usage) : null
        };
    }

    public object? GetService(System.Type serviceType, object? serviceKey = null)
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

[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string))]
internal sealed partial class AnthropicClientJsonContext : JsonSerializerContext;
