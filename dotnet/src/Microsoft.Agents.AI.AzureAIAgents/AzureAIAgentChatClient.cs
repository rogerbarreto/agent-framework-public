// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using Azure.AI.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;
using OpenAI.Responses;

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Microsoft.Agents.AI.AzureAIAgents;

/// <summary>
/// Provides a chat client implementation that integrates with Azure AI Agents, enabling chat interactions using
/// Azure-specific agent capabilities.
/// </summary>
internal sealed class AzureAIAgentChatClient : DelegatingChatClient
{
    private readonly ChatClientMetadata? _metadata;
    private readonly AgentsClient _agentsClient;
    private readonly AgentRecord _agentRecord;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAIAgentChatClient"/> class.
    /// </summary>
    /// <param name="agentsClient">An instance of <see cref="AgentsClient"/> to interact with Azure AI Agents services.</param>
    /// <param name="agentRecord">An instance of <see cref="AgentRecord"/> representing the specific agent to use.</param>
    /// <param name="model">The AI model to use for the chat client.</param>
    /// <remarks>
    /// The <see cref="IChatClient"/> provided should be decorated with a <see cref="AzureAIAgentChatClient"/> for proper functionality.
    /// </remarks>
    internal AzureAIAgentChatClient(AgentsClient agentsClient, AgentRecord agentRecord, string model)
        : base(agentsClient.GetOpenAIClient().GetOpenAIResponseClient(Throw.IfNull(model)).AsIChatClient())
    {
        this._agentsClient = Throw.IfNull(agentsClient);
        this._agentRecord = Throw.IfNull(agentRecord);
        this._metadata = new ChatClientMetadata("azure.ai.agents");
    }

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        return (serviceKey is null && serviceType == typeof(ChatClientMetadata))
            ? this._metadata
            : (serviceKey is null && serviceType == typeof(AgentsClient))
            ? this._agentsClient
            : (serviceKey is null && serviceType == typeof(AgentVersion))
            ? this._agentRecord.Versions.Latest
            : (serviceKey is null && serviceType == typeof(AgentRecord))
            ? this._agentRecord
            : base.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var conversation = await this.GetConversationAsync(messages, options, cancellationToken).ConfigureAwait(false);
        this.SetChatOptionsFactory(options, conversation);

        return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var conversation = await this.GetConversationAsync(messages, options, cancellationToken).ConfigureAwait(false);
        this.SetChatOptionsFactory(options, conversation);

        await foreach (var chunk in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    private async Task<AgentConversation> GetConversationAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
    {
        AgentConversation conversation;
        if (string.IsNullOrWhiteSpace(options?.ConversationId))
        {
            var creationOptions = new AgentConversationCreationOptions();
            if (creationOptions.Items is List<ResponseItem> itemsList)
            {
                itemsList.AddRange(messages.AsOpenAIResponseItems());
            }
            else
            {
                foreach (var item in messages.AsOpenAIResponseItems())
                {
                    creationOptions.Items.Add(item);
                }
            }

            conversation = await this._agentsClient.GetConversationsClient().CreateConversationAsync(creationOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            conversation = await this._agentsClient.GetConversationsClient().GetConversationAsync(options!.ConversationId, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return conversation;
    }

    private void SetChatOptionsFactory(ChatOptions? chatOptions, AgentConversation agentConversation)
    {
        chatOptions ??= new ChatOptions();
        chatOptions.RawRepresentationFactory = (client) =>
        {
            var rawRepresentationFactory = chatOptions.RawRepresentationFactory;
            ResponseCreationOptions? responseCreationOptions = null;

            if (rawRepresentationFactory is not null)
            {
                responseCreationOptions = rawRepresentationFactory.Invoke(this) as ResponseCreationOptions;

                if (responseCreationOptions is null)
                {
                    throw new InvalidOperationException("The provided ChatOptions RawRepresentationFactory did not return a valid ResponseCreationOptions instance.");
                }
            }
            else
            {
                responseCreationOptions = new ResponseCreationOptions();
            }

            responseCreationOptions.SetAgentReference(this._agentRecord.Name);
            responseCreationOptions.SetConversationReference(agentConversation);

            return responseCreationOptions;
        };
    }
}
