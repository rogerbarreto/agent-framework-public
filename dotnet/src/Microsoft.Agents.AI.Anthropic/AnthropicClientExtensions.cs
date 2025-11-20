// Copyright (c) Microsoft. All rights reserved.

using Anthropic;
using Anthropic.Services;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides extension methods for the <see cref="IAnthropicClient"/> class.
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
    /// <param name="tools">The tools available to the AI agent.</param>
    /// <param name="defaultMaxTokens">The default maximum tokens for chat completions. Defaults to <see cref="DefaultMaxTokens"/> if not provided.</param>
    /// <returns>The created <see cref="ChatClientAgent"/> AI agent.</returns>
    public static ChatClientAgent CreateAIAgent(
        this IAnthropicClient client,
        string model,
        string? instructions = null,
        string? name = null,
        string? description = null,
        IList<AITool>? tools = null,
        int? defaultMaxTokens = null)
    {
        var options = new ChatClientAgentOptions
        {
            Instructions = instructions,
            Name = name,
            Description = description,
        };

        if (tools is { Count: > 0 })
        {
            options.ChatOptions = new ChatOptions { Tools = tools };
        }

        return new ChatClientAgent(client.AsIChatClient(model, defaultMaxTokens), options);
    }

    /// <summary>
    /// Creates a new AI agent using the specified model and options.
    /// </summary>
    /// <param name="betaService">The Anthropic beta service.</param>
    /// <param name="model">The model to use for chat completions.</param>
    /// <param name="instructions">The instructions for the AI agent.</param>
    /// <param name="name">The name of the AI agent.</param>
    /// <param name="description">The description of the AI agent.</param>
    /// <param name="tools">The tools available to the AI agent.</param>
    /// <param name="defaultMaxTokens">The default maximum tokens for chat completions. Defaults to <see cref="DefaultMaxTokens"/> if not provided.</param>
    /// <returns>The created <see cref="ChatClientAgent"/> AI agent.</returns>
    public static ChatClientAgent CreateAIAgent(
        this IBetaService betaService,
        string model,
        string? instructions = null,
        string? name = null,
        string? description = null,
        IList<AITool>? tools = null,
        int? defaultMaxTokens = null)
    {
        var options = new ChatClientAgentOptions
        {
            Instructions = instructions,
            Name = name,
            Description = description,
        };

        if (tools is { Count: > 0 })
        {
            options.ChatOptions = new ChatOptions { Tools = tools };
        }

        return new ChatClientAgent(betaService.AsIChatClient(model, defaultMaxTokens), options);
    }
}
