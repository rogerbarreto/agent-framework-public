// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Anthropic;
using Microsoft.Extensions.AI;

namespace Anthropic.Client;

/// <summary>
/// Provides extension methods for the <see cref="AnthropicClient"/> class.
/// </summary>
public static class AnthropicClientExtensions
{
    /// <summary>
    /// Specifies the default maximum number of tokens allowed for processing operations.
    /// </summary>
    public static long DefaultMaxTokens { get; set; } = 4096;

    /// <summary>
    /// Creates a new AI agent using the specified model and options.
    /// </summary>
    /// <param name="client">The Anthropic client.</param>
    /// <param name="model">The model to use for chat completions.</param>
    /// <param name="instructions">The instructions for the AI agent.</param>
    /// <param name="name">The name of the AI agent.</param>
    /// <param name="description">The description of the AI agent.</param>
    /// <param name="defaultMaxTokens">The default maximum tokens for chat completions. Defaults to <see cref="DefaultMaxTokens"/> if not provided.</param>
    /// <returns>The created <see cref="ChatClientAgent"/> AI agent.</returns>
    public static ChatClientAgent CreateAIAgent(
        this AnthropicClient client,
        string model,
        string? instructions,
        string? name = null,
        string? description = null,
        long? defaultMaxTokens = null)
    {
        var options = new ChatClientAgentOptions
        {
            Instructions = instructions,
            Name = name,
            Description = description,
        };

        return new ChatClientAgent(client.AsIChatClient(model, defaultMaxTokens), options);
    }

    /// <summary>
    /// Get an <see cref="IChatClient"/> compatible implementation around the <see cref="AnthropicClient"/>.
    /// </summary>
    /// <param name="client">The Anthropic client.</param>
    /// <param name="defaultModelId">The default model to use for chat completions.</param>
    /// <param name="defaultMaxTokens">The default maximum tokens for chat completions. Defaults to <see cref="DefaultMaxTokens"/> if not provided.</param>
    /// <returns>The <see cref="IChatClient"/> implementation.</returns>
    public static IChatClient AsIChatClient(
        this AnthropicClient client,
        string defaultModelId,
        long? defaultMaxTokens = null)
    {
        return new AnthropicBetaChatClient(client, defaultMaxTokens ?? DefaultMaxTokens, defaultModelId: defaultModelId);
    }
}
