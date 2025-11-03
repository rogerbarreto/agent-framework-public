// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using Azure.AI.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;
using OpenAI;
using OpenAI.Responses;

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Microsoft.Agents.AI.AzureAI;

/// <summary>
/// Provides a chat client implementation that integrates with Azure AI Agents, enabling chat interactions using
/// Azure-specific agent capabilities.
/// </summary>
internal sealed class AzureAIAgentChatClient : DelegatingChatClient
{
    private readonly ChatClientMetadata? _metadata;
    private readonly AgentsClient _agentsClient;
    private readonly AgentVersion _agentVersion;
    private readonly IList<AITool>? _tools;

    /// <summary>
    /// The usage of a no-op model is a necessary change to avoid OpenAIClients to throw exceptions when
    /// used with Azure AI Agents as the model used is now defined at the agent creation time.
    /// </summary>
    private const string NoOpModel = "no-op";

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAIAgentChatClient"/> class.
    /// </summary>
    /// <param name="agentsClient">An instance of <see cref="AgentsClient"/> to interact with Azure AI Agents services.</param>
    /// <param name="agentRecord">An instance of <see cref="AgentRecord"/> representing the specific agent to use.</param>
    /// <param name="tools">An optional list of <see cref="AITool"/>s to be used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <remarks>
    /// The <see cref="IChatClient"/> provided should be decorated with a <see cref="AzureAIAgentChatClient"/> for proper functionality.
    /// </remarks>
    internal AzureAIAgentChatClient(AgentsClient agentsClient, AgentRecord agentRecord, IList<AITool>? tools = null, OpenAIClientOptions? openAIClientOptions = null)
        : this(agentsClient, Throw.IfNull(agentRecord).Versions.Latest, tools, openAIClientOptions)
    {
    }

    internal AzureAIAgentChatClient(AgentsClient agentsClient, AgentVersion agentVersion, IList<AITool>? tools = null, OpenAIClientOptions? openAIClientOptions = null)
        : base(agentsClient
            .GetOpenAIClient(openAIClientOptions)
            .GetOpenAIResponseClient((agentVersion.Definition as PromptAgentDefinition)?.Model ?? NoOpModel)
            .AsIChatClient())
    {
        this._agentsClient = Throw.IfNull(agentsClient);
        this._agentVersion = Throw.IfNull(agentVersion);
        this._metadata = new ChatClientMetadata("azure.ai.agents");

        this.EnsureInProcToolsAvailable(tools);
        this._tools = tools;
    }

    /// <summary>
    /// This method validates if all in-proc tools are provided at the chat client creation matches the requirement of the agent definition.
    /// </summary>
    /// <param name="tools">Chat client provided tools.</param>
    /// <exception cref="InvalidOperationException">Agent definition requires tools that were not provided.</exception>
    /// <exception cref="InvalidOperationException"><see cref="FunctionInvokingChatClient"/> is not available in the client stack.</exception>
    private void EnsureInProcToolsAvailable(IList<AITool>? tools)
    {
        if (this._agentVersion.Definition is PromptAgentDefinition definition && definition.Tools is { Count: > 0 })
        {
            // The chat client was instantiated with no tools while the agent definition requires in-proc tools.
            if (tools is null or { Count: 0 } && definition.Tools.Any(t => t is FunctionTool))
            {
                throw new InvalidOperationException("The agent definition requires in-process tools but none were provided.");
            }

            // Validate that all required tools are provided.
            List<string> missingTools = [];

            // Check function tools
            foreach (FunctionTool functionTool in definition.Tools.Where(t => t is FunctionTool))
            {
                // Check if a tool with the same type and name exists in the provided tools.
                var matchingTool = tools?.FirstOrDefault(t => t is AIFunction tf && functionTool.FunctionName == tf.Name);
                if (matchingTool is null)
                {
                    missingTools.Add($"Function tool: {functionTool.FunctionName}");
                }
            }

            if (missingTools.Count > 0)
            {
                throw new InvalidOperationException($"The following prompt agent definition required tools were not provided: {string.Join(", ", missingTools)}");
            }
        }
    }

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        return (serviceKey is null && serviceType == typeof(ChatClientMetadata))
            ? this._metadata
            : (serviceKey is null && serviceType == typeof(AgentsClient))
            ? this._agentsClient
            : (serviceKey is null && serviceType == typeof(AgentVersion))
            ? this._agentVersion
            : base.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Add the tools to the FICC.
        this.AddToolsOnceToFunctionInvokingChatClient();

        var conversation = await this.GetOrCreateConversationAsync(messages, options, cancellationToken).ConfigureAwait(false);
        var conversationOptions = this.GetConversationEnabledChatOptions(options, conversation);

        return await base.GetResponseAsync(messages, conversationOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Add the tools to the FICC.
        this.AddToolsOnceToFunctionInvokingChatClient();

        var conversation = await this.GetOrCreateConversationAsync(messages, options, cancellationToken).ConfigureAwait(false);
        var conversationOptions = this.GetConversationEnabledChatOptions(options, conversation);

        await foreach (var chunk in base.GetStreamingResponseAsync(messages, conversationOptions, cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Attempt to adds the tools provided at construction time to the FunctionInvokingChatClient provided in the client stack if it doesn't already have.
    /// </summary>
    /// <exception cref="InvalidOperationException">To use tools the FunctionInvokingChatClient is required in the client stack.</exception>
    private void AddToolsOnceToFunctionInvokingChatClient()
    {
        var ficc = this.InnerClient.GetService<FunctionInvokingChatClient>()
            ?? throw new InvalidOperationException("To use tools the FunctionInvokingChatClient is required in the client stack.");

        // This implementation 
        // Only set the tools if they were not already set.
        ficc.AdditionalTools ??= this._tools;
    }

    private async Task<AgentConversation> GetOrCreateConversationAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(options?.ConversationId)
            ? await this._agentsClient.GetConversationClient().CreateConversationAsync(cancellationToken: cancellationToken).ConfigureAwait(false)
            : await this._agentsClient.GetConversationClient().GetConversationAsync(options.ConversationId, cancellationToken: cancellationToken).ConfigureAwait(false);

    private ChatOptions GetConversationEnabledChatOptions(ChatOptions? chatOptions, AgentConversation agentConversation)
    {
        // Ignore all the chatOptions provided as agents options can't be set per-request basis.
        var conversationChatOptions = new ChatOptions();

        // Preserve the original RawRepresentationFactory
        var originalFactory = chatOptions?.RawRepresentationFactory;

        conversationChatOptions.RawRepresentationFactory = (client) =>
        {
            if (originalFactory?.Invoke(this) is not ResponseCreationOptions responseCreationOptions)
            {
                responseCreationOptions = new ResponseCreationOptions();
            }

            responseCreationOptions.SetAgentReference(this._agentVersion.Name);
            responseCreationOptions.SetConversationReference(agentConversation);

            return responseCreationOptions;
        };

        return conversationChatOptions;
    }
}
