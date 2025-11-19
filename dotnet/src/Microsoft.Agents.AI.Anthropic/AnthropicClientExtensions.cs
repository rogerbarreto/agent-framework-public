// Copyright (c) Microsoft. All rights reserved.

using Anthropic.Models.Beta.Messages;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Anthropic;
using Microsoft.Extensions.AI;

namespace Anthropic;

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
    /// <param name="tools">The tools available to the AI agent.</param>
    /// <param name="defaultMaxTokens">The default maximum tokens for chat completions. Defaults to <see cref="DefaultMaxTokens"/> if not provided.</param>
    /// <returns>The created <see cref="ChatClientAgent"/> AI agent.</returns>
    public static ChatClientAgent CreateAIAgent(
        this AnthropicClient client,
        string model,
        string? instructions,
        string? name = null,
        string? description = null,
        IList<AITool>? tools = null,
        long? defaultMaxTokens = null)
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

    /// <summary>Creates an <see cref="AITool"/> to represent a raw <see cref="BetaTool"/>.</summary>
    /// <param name="tool">The tool to wrap as an <see cref="AITool"/>.</param>
    /// <returns>The <paramref name="tool"/> wrapped as an <see cref="AITool"/>.</returns>
    /// <remarks>
    /// <para>
    /// The returned tool is only suitable for use with the <see cref="IChatClient"/> returned by
    /// <see cref="AsIChatClient(AnthropicClient, string, long?)"/> (or <see cref="IChatClient"/>s that delegate
    /// to such an instance). It is likely to be ignored by any other <see cref="IChatClient"/> implementation.
    /// </para>
    /// <para>
    /// When a tool has a corresponding <see cref="AITool"/>-derived type already defined in Microsoft.Extensions.AI,
    /// such as <see cref="AIFunction"/>, <see cref="HostedWebSearchTool"/>, <see cref="HostedMcpServerTool"/>, or
    /// <see cref="HostedFileSearchTool"/>, those types should be preferred instead of this method, as they are more portable,
    /// capable of being respected by any <see cref="IChatClient"/> implementation. This method does not attempt to
    /// map the supplied <see cref="BetaTool"/> to any of those types, it simply wraps it as-is:
    /// the <see cref="IChatClient"/> returned by <see cref="AsIChatClient(AnthropicClient, string, long?)"/> will
    /// be able to unwrap the <see cref="BetaTool"/> when it processes the list of tools.
    /// </para>
    /// </remarks>
    public static AITool AsAITool(this BetaTool tool)
    {
        return new AnthropicBetaChatClient.BetaToolAITool(tool);
    }
}
