// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.AzureAIAgents;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;
using OpenAI.Responses;

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Azure.AI.Agents;

/// <summary>
/// Provides extension methods for <see cref="AgentsClient"/>.
/// </summary>
public static class AgentsClientExtensions
{
    /*
    /// <summary>
    /// Gets a runnable agent instance from the provided response containing persistent agent metadata.
    /// </summary>
    /// <param name="client">The client used to interact with persistent agents. Cannot be <see langword="null"/>.</param>
    /// <param name="persistentAgentResponse">The response containing the persistent agent to be converted. Cannot be <see langword="null"/>.</param>
    /// <param name="chatOptions">The default <see cref="ChatOptions"/> to use when interacting with the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the persistent agent.</returns>
    public static ChatClientAgent GetAIAgent(this AgentsClient client, Response<PersistentAgent> persistentAgentResponse, ChatOptions? chatOptions = null, Func<IChatClient, IChatClient>? clientFactory = null)
    {
        if (persistentAgentResponse is null)
        {
            throw new ArgumentNullException(nameof(persistentAgentResponse));
        }

        return GetAIAgent(client, persistentAgentResponse.Value, chatOptions, clientFactory);
    }

    /// <summary>
    /// Gets a runnable agent instance from a <see cref="PersistentAgent"/> containing metadata about a persistent agent.
    /// </summary>
    /// <param name="persistentAgentsClient">The client used to interact with persistent agents. Cannot be <see langword="null"/>.</param>
    /// <param name="persistentAgentMetadata">The persistent agent metadata to be converted. Cannot be <see langword="null"/>.</param>
    /// <param name="chatOptions">The default <see cref="ChatOptions"/> to use when interacting with the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the persistent agent.</returns>
    public static ChatClientAgent GetAIAgent(this PersistentAgentsClient persistentAgentsClient, PersistentAgent persistentAgentMetadata, ChatOptions? chatOptions = null, Func<IChatClient, IChatClient>? clientFactory = null)
    {
        if (persistentAgentMetadata is null)
        {
            throw new ArgumentNullException(nameof(persistentAgentMetadata));
        }

        if (persistentAgentsClient is null)
        {
            throw new ArgumentNullException(nameof(persistentAgentsClient));
        }

        var chatClient = persistentAgentsClient.AsIChatClient(persistentAgentMetadata.Id);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        return new ChatClientAgent(chatClient, options: new()
        {
            Id = persistentAgentMetadata.Id,
            Name = persistentAgentMetadata.Name,
            Description = persistentAgentMetadata.Description,
            Instructions = persistentAgentMetadata.Instructions,
            ChatOptions = chatOptions
        });
    }

    /// <summary>
    /// Retrieves an existing server side agent, wrapped as a <see cref="ChatClientAgent"/> using the provided <see cref="PersistentAgentsClient"/>.
    /// </summary>
    /// <param name="persistentAgentsClient">The <see cref="PersistentAgentsClient"/> to create the <see cref="ChatClientAgent"/> with.</param>
    /// <returns>A <see cref="ChatClientAgent"/> for the persistent agent.</returns>
    /// <param name="agentId"> The ID of the server side agent to create a <see cref="ChatClientAgent"/> for.</param>
    /// <param name="chatOptions">Options that should apply to all runs of the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the persistent agent.</returns>
    public static ChatClientAgent GetAIAgent(
        this PersistentAgentsClient persistentAgentsClient,
        string agentId,
        ChatOptions? chatOptions = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        CancellationToken cancellationToken = default)
    {
        if (persistentAgentsClient is null)
        {
            throw new ArgumentNullException(nameof(persistentAgentsClient));
        }

        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException($"{nameof(agentId)} should not be null or whitespace.", nameof(agentId));
        }

        var persistentAgentResponse = persistentAgentsClient.Administration.GetAgent(agentId, cancellationToken);
        return persistentAgentsClient.GetAIAgent(persistentAgentResponse, chatOptions, clientFactory);
    }

    /// <summary>
    /// Retrieves an existing server side agent, wrapped as a <see cref="ChatClientAgent"/> using the provided <see cref="PersistentAgentsClient"/>.
    /// </summary>
    /// <param name="agentsClient">The <see cref="AgentsClient"/> to create the <see cref="ChatClientAgent"/> with.</param>
    /// <returns>A <see cref="ChatClientAgent"/> for the persistent agent.</returns>
    /// <param name="name"> The ID of the server side agent to create a <see cref="ChatClientAgent"/> for.</param>
    /// <param name="chatOptions">Options that should apply to all runs of the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the persistent agent.</returns>
    public static async Task<ChatClientAgent> GetAIAgentAsync(
        this AgentsClient agentsClient,
        string name,
        ChatOptions? chatOptions = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNullOrWhitespace(name);

        var agent = await agentsClient.GetAgentAsync(name, cancellationToken).ConfigureAwait(false);
        if (agent is null)
        {
            agent.ver
            throw new InvalidOperationException($"Agent with name '{name}' not found.");
        }

        var persistentAgentResponse = await agentsClient.Administration.GetAgentAsync(name, cancellationToken).ConfigureAwait(false);
        return agentsClient.GetAIAgent(persistentAgentResponse, chatOptions, clientFactory);
    }

    /// <summary>
    /// Gets a runnable agent instance from the provided response containing persistent agent metadata.
    /// </summary>
    /// <param name="persistentAgentsClient">The client used to interact with persistent agents. Cannot be <see langword="null"/>.</param>
    /// <param name="persistentAgentResponse">The response containing the persistent agent to be converted. Cannot be <see langword="null"/>.</param>
    /// <param name="options">Full set of options to configure the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the persistent agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="persistentAgentResponse"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static ChatClientAgent GetAIAgent(this PersistentAgentsClient persistentAgentsClient, Response<PersistentAgent> persistentAgentResponse, ChatClientAgentOptions options, Func<IChatClient, IChatClient>? clientFactory = null)
    {
        if (persistentAgentResponse is null)
        {
            throw new ArgumentNullException(nameof(persistentAgentResponse));
        }

        return GetAIAgent(persistentAgentsClient, persistentAgentResponse.Value, options, clientFactory);
    }

    /// <summary>
    /// Gets a runnable agent instance from a <see cref="PersistentAgent"/> containing metadata about a persistent agent.
    /// </summary>
    /// <param name="persistentAgentsClient">The client used to interact with persistent agents. Cannot be <see langword="null"/>.</param>
    /// <param name="persistentAgentMetadata">The persistent agent metadata to be converted. Cannot be <see langword="null"/>.</param>
    /// <param name="options">Full set of options to configure the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the persistent agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="persistentAgentMetadata"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static ChatClientAgent GetAIAgent(this PersistentAgentsClient persistentAgentsClient, PersistentAgent persistentAgentMetadata, ChatClientAgentOptions options, Func<IChatClient, IChatClient>? clientFactory = null)
    {
        if (persistentAgentMetadata is null)
        {
            throw new ArgumentNullException(nameof(persistentAgentMetadata));
        }

        if (persistentAgentsClient is null)
        {
            throw new ArgumentNullException(nameof(persistentAgentsClient));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var chatClient = persistentAgentsClient.AsIChatClient(persistentAgentMetadata.Id);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        var agentOptions = new ChatClientAgentOptions()
        {
            Id = persistentAgentMetadata.Id,
            Name = options.Name ?? persistentAgentMetadata.Name,
            Description = options.Description ?? persistentAgentMetadata.Description,
            Instructions = options.Instructions ?? persistentAgentMetadata.Instructions,
            ChatOptions = options.ChatOptions,
            AIContextProviderFactory = options.AIContextProviderFactory,
            ChatMessageStoreFactory = options.ChatMessageStoreFactory,
            UseProvidedChatClientAsIs = options.UseProvidedChatClientAsIs
        };

        return new ChatClientAgent(chatClient, agentOptions);
    }

    /// <summary>
    /// Retrieves an existing server side agent, wrapped as a <see cref="ChatClientAgent"/> using the provided <see cref="PersistentAgentsClient"/>.
    /// </summary>
    /// <param name="persistentAgentsClient">The <see cref="PersistentAgentsClient"/> to create the <see cref="ChatClientAgent"/> with.</param>
    /// <param name="agentId">The ID of the server side agent to create a <see cref="ChatClientAgent"/> for.</param>
    /// <param name="options">Full set of options to configure the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the persistent agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="persistentAgentsClient"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="agentId"/> is empty or whitespace.</exception>
    public static ChatClientAgent GetAIAgent(
        this PersistentAgentsClient persistentAgentsClient,
        string agentId,
        ChatClientAgentOptions options,
        Func<IChatClient, IChatClient>? clientFactory = null,
        CancellationToken cancellationToken = default)
    {
        if (persistentAgentsClient is null)
        {
            throw new ArgumentNullException(nameof(persistentAgentsClient));
        }

        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException($"{nameof(agentId)} should not be null or whitespace.", nameof(agentId));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var persistentAgentResponse = persistentAgentsClient.Administration.GetAgent(agentId, cancellationToken);
        return persistentAgentsClient.GetAIAgent(persistentAgentResponse, options, clientFactory);
    }

    /// <summary>
    /// Retrieves an existing server side agent, wrapped as a <see cref="ChatClientAgent"/> using the provided <see cref="PersistentAgentsClient"/>.
    /// </summary>
    /// <param name="persistentAgentsClient">The <see cref="PersistentAgentsClient"/> to create the <see cref="ChatClientAgent"/> with.</param>
    /// <param name="agentId">The ID of the server side agent to create a <see cref="ChatClientAgent"/> for.</param>
    /// <param name="options">Full set of options to configure the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the persistent agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="persistentAgentsClient"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="agentId"/> is empty or whitespace.</exception>
    public static async Task<ChatClientAgent> GetAIAgentAsync(
        this PersistentAgentsClient persistentAgentsClient,
        string agentId,
        ChatClientAgentOptions options,
        Func<IChatClient, IChatClient>? clientFactory = null,
        CancellationToken cancellationToken = default)
    {
        if (persistentAgentsClient is null)
        {
            throw new ArgumentNullException(nameof(persistentAgentsClient));
        }

        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException($"{nameof(agentId)} should not be null or whitespace.", nameof(agentId));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var persistentAgentResponse = await persistentAgentsClient.Administration.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        return persistentAgentsClient.GetAIAgent(persistentAgentResponse, options, clientFactory);
    }*/

    /// <summary>
    /// Creates a new server side agent using the provided <see cref="AgentsClient"/>.
    /// </summary>
    /// <param name="agentsClient">The <see cref="AgentsClient"/> to create the agent with.</param>
    /// <param name="model">The model to be used by the agent.</param>
    /// <param name="name">The name of the agent.</param>
    /// <param name="instructions">The instructions for the agent.</param>
    /// <param name="tools">The tools to be used by the agent.</param>
    /// <param name="temperature">The temperature setting for the agent.</param>
    /// <param name="topP">The top-p setting for the agent.</param>
    /// <param name="raiConfig">The responsible AI configuration for the agent.</param>
    /// <param name="reasoningOptions">The reasoning options for the agent.</param>
    /// <param name="textOptions">The text options for the agent.</param>
    /// <param name="structuredInputs">The structured inputs for the agent.</param>
    /// <param name="metadata">The metadata for the agent.</param>
    /// <param name="clientFactory">A factory function to customize the creation of the chat client used by the agent.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="AIAgent"/> instance that can be used to perform operations on the newly created agent.</returns>
    public static ChatClientAgent CreateAIAgent(
        this AgentsClient agentsClient,
        string model,
        string? name = null,
        string? instructions = null,
        IEnumerable<ResponseTool>? tools = null,
        float? temperature = null,
        float? topP = null,
        RaiConfig? raiConfig = null,
        ResponseReasoningOptions? reasoningOptions = null,
        ResponseTextOptions? textOptions = null,
        IDictionary<string, StructuredInputDefinition>? structuredInputs = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNull(model);

        var (promptAgentDefinition, creationOptions) = CreatePromptAgentDefinitionAndOptions(
            model, instructions, temperature, topP, raiConfig, reasoningOptions, textOptions, tools, structuredInputs, metadata);

        AgentRecord agentRecord = agentsClient.CreateAgent(name, promptAgentDefinition, creationOptions, cancellationToken);
        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentRecord, model);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        return new ChatClientAgent(chatClient);
    }

    /// <summary>
    /// Creates a new AI agent using the specified agent definition and optional configuration parameters.
    /// </summary>
    /// <param name="agentsClient">The client used to manage and interact with AI agents.</param>
    /// <param name="agentDefinition">The definition that specifies the configuration and behavior of the agent to create.</param>
    /// <param name="model">The name of the model to use for the agent. If not specified, the model must be provided as part of the agent definition.</param>
    /// <param name="name">The name for the agent.</param>
    /// <param name="creationOptions">Settings that control the creation of the agent.</param>
    /// <param name="clientFactory">A factory function to customize the creation of the chat client used by the agent.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="AIAgent"/> instance that can be used to perform operations on the newly created agent.</returns>
    /// <exception cref="ArgumentException">Thrown if neither the 'model' parameter nor a model in the agent definition is provided.</exception>
    public static ChatClientAgent CreateAIAgent(
        this AgentsClient agentsClient,
        AgentDefinition agentDefinition,
        string? model = null,
        string? name = null,
        AgentCreationOptions? creationOptions = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNull(agentDefinition);

        model ??= (agentDefinition as PromptAgentDefinition)?.Model;
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model must be provided either directly or as part of a PromptAgentDefinition specialization.", nameof(model));
        }

        AgentRecord agentRecord = agentsClient.CreateAgent(name, agentDefinition, creationOptions, cancellationToken);
        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentRecord, model);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        return new ChatClientAgent(chatClient);
    }

    /// <summary>
    /// Creates a new Prompt AI Agent using the provided <see cref="AgentsClient"/> and options.
    /// </summary>
    /// <param name="agentsClient">The client used to manage and interact with AI agents.</param>
    /// <param name="model">The name of the model to use for the agent. If not specified, the model must be provided as part of the agent definition.</param>
    /// <param name="options">The options for creating the agent.</param>
    /// <param name="clientFactory">A factory function to customize the creation of the chat client used by the agent.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the newly created agent.</returns>
    /// <exception cref="ArgumentException">Thrown if neither the 'model' parameter nor a model in the agent definition is provided.</exception>
    public static ChatClientAgent CreateAIAgent(
        this AgentsClient agentsClient,
        string model,
        ChatClientAgentOptions options,
        Func<IChatClient, IChatClient>? clientFactory = null)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNullOrWhitespace(model);
        Throw.IfNull(options);

        PromptAgentDefinition promptAgentDefinition = new(model)
        {
            Model = model,
            Instructions = options.Instructions,
        };

        if (options.ChatOptions?.Tools is { Count: > 0 })
        {
            foreach (var tool in options.ChatOptions.Tools)
            {
                promptAgentDefinition.Tools.Add(ToResponseTool(tool, options.ChatOptions));
            }
        }

        AgentCreationOptions creationOptions = new();

        AgentRecord agentRecord = agentsClient.CreateAgent(options.Name, promptAgentDefinition, creationOptions);
        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentRecord, model);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        return new ChatClientAgent(chatClient);
    }

    /// <summary>
    /// Asynchronously creates a new AI agent using the specified agent definition and optional configuration
    /// parameters.
    /// </summary>
    /// <param name="agentsClient">The client used to manage and interact with AI agents.</param>
    /// <param name="agentDefinition">The definition that specifies the configuration and behavior of the agent to create.</param>
    /// <param name="model">The name of the model to use for the agent. If not specified, the model must be provided as part of the agent definition.</param>
    /// <param name="name">The name for the agent.</param>
    /// <param name="creationOptions">Settings that control the creation of the agent.</param>
    /// <param name="clientFactory">A factory function to customize the creation of the chat client used by the agent.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the created AI agent.</returns>
    /// <exception cref="ArgumentException">Thrown if neither the 'model' parameter nor a model in the agent definition is provided.</exception>
    public static async Task<AIAgent> CreateAIAgentAsync(
        this AgentsClient agentsClient,
        AgentDefinition agentDefinition,
        string? model = null,
        string? name = null,
        AgentCreationOptions? creationOptions = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNull(agentDefinition);

        model ??= (agentDefinition as PromptAgentDefinition)?.Model;
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model must be provided either directly or as part of a PromptAgentDefinition specialization.", nameof(model));
        }

        AgentRecord agentRecord = await agentsClient.CreateAgentAsync(name, agentDefinition, creationOptions, cancellationToken).ConfigureAwait(false);
        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentRecord, model);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        return new ChatClientAgent(chatClient);
    }

    /// <summary>
    /// Creates a new server side prompt agent using the provided <see cref="AgentsClient"/>.
    /// </summary>
    /// <param name="agentsClient">The <see cref="AgentsClient"/> to create the agent with.</param>
    /// <param name="model">The model to be used by the agent.</param>
    /// <param name="name">The name of the agent.</param>
    /// <param name="instructions">The instructions for the agent.</param>
    /// <param name="tools">The tools to be used by the agent.</param>
    /// <param name="temperature">The temperature setting for the agent.</param>
    /// <param name="topP">The top-p setting for the agent.</param>
    /// <param name="raiConfig">The responsible AI configuration for the agent.</param>
    /// <param name="reasoningOptions">The reasoning options for the agent.</param>
    /// <param name="textOptions">The text options for the agent.</param>
    /// <param name="structuredInputs">The structured inputs for the agent.</param>
    /// <param name="metadata">The metadata for the agent.</param>
    /// <param name="clientFactory">A factory function to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a <see cref="AIAgent"/> instance that can be used to perform operations on the newly created agent.</returns>
    public static async Task<AIAgent> CreateAIAgentAsync(
        this AgentsClient agentsClient,
        string model,
        string? name = null,
        string? instructions = null,
        IEnumerable<ResponseTool>? tools = null,
        float? temperature = null,
        float? topP = null,
        RaiConfig? raiConfig = null,
        ResponseReasoningOptions? reasoningOptions = null,
        ResponseTextOptions? textOptions = null,
        IDictionary<string, StructuredInputDefinition>? structuredInputs = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNull(model);

        var (promptAgentDefinition, creationOptions) = CreatePromptAgentDefinitionAndOptions(
            model, instructions, temperature, topP, raiConfig, reasoningOptions, textOptions, tools, structuredInputs, metadata);

        AgentRecord agentRecord = await agentsClient.CreateAgentAsync(name, promptAgentDefinition, creationOptions, cancellationToken).ConfigureAwait(false);
        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentRecord, model);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        return new ChatClientAgent(chatClient);
    }

    #region Private

    private static (PromptAgentDefinition, AgentCreationOptions?) CreatePromptAgentDefinitionAndOptions(
        string model,
        string? instructions,
        float? temperature,
        float? topP,
        RaiConfig? raiConfig,
        ResponseReasoningOptions? reasoningOptions,
        ResponseTextOptions? textOptions,
        IEnumerable<ResponseTool>? tools,
        IDictionary<string, StructuredInputDefinition>? structuredInputs,
        IReadOnlyDictionary<string, string>? metadata)
    {
        PromptAgentDefinition promptAgentDefinition = new(model)
        {
            Model = model,
            Instructions = instructions,
            Temperature = temperature,
            TopP = topP,
            RaiConfig = raiConfig,
            ReasoningOptions = reasoningOptions,
            TextOptions = textOptions,
        };

        AgentCreationOptions? creationOptions = null;
        if (metadata is not null)
        {
            creationOptions = new();
            foreach (var kvp in metadata)
            {
                creationOptions.Metadata.Add(kvp.Key, kvp.Value);
            }
        }

        if (tools is not null)
        {
            if (promptAgentDefinition.Tools is List<ResponseTool> toolsList)
            {
                toolsList.AddRange(tools);
            }
            else
            {
                foreach (var tool in tools)
                {
                    promptAgentDefinition.Tools.Add(tool);
                }
            }
        }

        if (structuredInputs is not null)
        {
            foreach (var kvp in structuredInputs)
            {
                promptAgentDefinition.StructuredInputs.Add(kvp.Key, kvp.Value);
            }
        }

        return (promptAgentDefinition, creationOptions);
    }

    /// <summary>Key into AdditionalProperties used to store a strict option.</summary>
    private const string StrictKey = "strictJsonSchema";

    private static FunctionTool ToResponseFunctionTool(AIFunctionDeclaration aiFunction, ChatOptions? options = null)
    {
        bool? strict =
            HasStrict(aiFunction.AdditionalProperties) ??
            HasStrict(options?.AdditionalProperties);

        return ResponseTool.CreateFunctionTool(
            aiFunction.Name,
            ToOpenAIFunctionParameters(aiFunction, strict),
            strict,
            aiFunction.Description);
    }

    /// <summary>Gets whether the properties specify that strict schema handling is desired.</summary>
    private static bool? HasStrict(IReadOnlyDictionary<string, object?>? additionalProperties) =>
        additionalProperties?.TryGetValue(StrictKey, out object? strictObj) is true &&
        strictObj is bool strictValue ?
        strictValue : null;

    /// <summary>Extracts from an <see cref="AIFunctionDeclaration"/> the parameters and strictness setting for use with OpenAI's APIs.</summary>
    private static BinaryData ToOpenAIFunctionParameters(AIFunctionDeclaration aiFunction, bool? strict)
    {
        // Perform any desirable transformations on the function's JSON schema, if it'll be used in a strict setting.
        JsonElement jsonSchema = strict is true ?
            GetStrictSchemaTransformCache().GetOrCreateTransformedSchema(aiFunction) :
            aiFunction.JsonSchema;

        // Roundtrip the schema through the ToolJson model type to remove extra properties
        // and force missing ones into existence, then return the serialized UTF8 bytes as BinaryData.
        var tool = JsonSerializer.Deserialize(jsonSchema, AgentsClientJsonContext.Default.ToolJson)!;
        return BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(tool, AgentsClientJsonContext.Default.ToolJson));
    }

    /// <summary>
    /// Gets the JSON schema transformer cache conforming to OpenAI <b>strict</b> / structured output restrictions per
    /// https://platform.openai.com/docs/guides/structured-outputs?api-mode=responses#supported-schemas.
    /// </summary>
    private static AIJsonSchemaTransformCache GetStrictSchemaTransformCache() => new(new()
    {
        DisallowAdditionalProperties = true,
        ConvertBooleanSchemas = true,
        MoveDefaultKeywordToDescription = true,
        RequireAllProperties = true,
        TransformSchemaNode = (ctx, node) =>
        {
            // Move content from common but unsupported properties to description. In particular, we focus on properties that
            // the AIJsonUtilities schema generator might produce and/or that are explicitly mentioned in the OpenAI documentation.

            if (node is JsonObject schemaObj)
            {
                StringBuilder? additionalDescription = null;

                ReadOnlySpan<string> unsupportedProperties =
                [
                    // Produced by AIJsonUtilities but not in allow list at https://platform.openai.com/docs/guides/structured-outputs#supported-properties:
                    "contentEncoding", "contentMediaType", "not",

                    // Explicitly mentioned at https://platform.openai.com/docs/guides/structured-outputs?api-mode=responses#key-ordering as being unsupported with some models:
                    "minLength", "maxLength", "pattern", "format",
                    "minimum", "maximum", "multipleOf",
                    "patternProperties",
                    "minItems", "maxItems",

                    // Explicitly mentioned at https://learn.microsoft.com/azure/ai-services/openai/how-to/structured-outputs?pivots=programming-language-csharp&tabs=python-secure%2Cdotnet-entra-id#unsupported-type-specific-keywords
                    // as being unsupported with Azure OpenAI:
                    "unevaluatedProperties", "propertyNames", "minProperties", "maxProperties",
                    "unevaluatedItems", "contains", "minContains", "maxContains", "uniqueItems",
                ];

                foreach (string propName in unsupportedProperties)
                {
                    if (schemaObj[propName] is { } propNode)
                    {
                        _ = schemaObj.Remove(propName);
                        AppendLine(ref additionalDescription, propName, propNode);
                    }
                }

                if (additionalDescription is not null)
                {
                    schemaObj["description"] = schemaObj["description"] is { } descriptionNode && descriptionNode.GetValueKind() == JsonValueKind.String ?
                        $"{descriptionNode.GetValue<string>()}{Environment.NewLine}{additionalDescription}" :
                        additionalDescription.ToString();
                }

                return node;

                static void AppendLine(ref StringBuilder? sb, string propName, JsonNode propNode)
                {
                    sb ??= new();

                    if (sb.Length > 0)
                    {
                        _ = sb.AppendLine();
                    }

                    _ = sb.Append(propName).Append(": ").Append(propNode);
                }
            }

            return node;
        },
    });

    private static ResponseTool ToResponseTool(AITool tool, ChatOptions options)
    {
        switch (tool)
        {
            case AIFunctionDeclaration aiFunction:
                return ToResponseFunctionTool(aiFunction, options);

            case HostedWebSearchTool webSearchTool:
                WebSearchToolLocation? location = null;
                if (webSearchTool.AdditionalProperties.TryGetValue(nameof(WebSearchToolLocation), out object? objLocation))
                {
                    location = objLocation as WebSearchToolLocation;
                }

                WebSearchToolContextSize? size = null;
                if (webSearchTool.AdditionalProperties.TryGetValue(nameof(WebSearchToolContextSize), out object? objSize) &&
                    objSize is WebSearchToolContextSize)
                {
                    size = (WebSearchToolContextSize)objSize;
                }

                return ResponseTool.CreateWebSearchTool(location, size);

            case HostedFileSearchTool fileSearchTool:
                return ResponseTool.CreateFileSearchTool(
                    fileSearchTool.Inputs?.OfType<HostedVectorStoreContent>().Select(c => c.VectorStoreId) ?? [],
                    fileSearchTool.MaximumResultCount);

            case HostedCodeInterpreterTool codeTool:
                return ResponseTool.CreateCodeInterpreterTool(
                    new CodeInterpreterToolContainer(codeTool.Inputs?.OfType<HostedFileContent>().Select(f => f.FileId).ToList() is { Count: > 0 } ids ?
                            CodeInterpreterToolContainerConfiguration.CreateAutomaticContainerConfiguration(ids) :
                            new()));

            case HostedMcpServerTool mcpTool:
                McpTool responsesMcpTool = Uri.TryCreate(mcpTool.ServerAddress, UriKind.Absolute, out Uri? url) ?
                    ResponseTool.CreateMcpTool(
                        mcpTool.ServerName,
                        url,
                        mcpTool.AuthorizationToken,
                        mcpTool.ServerDescription) :
                    ResponseTool.CreateMcpTool(
                        mcpTool.ServerName,
                        new McpToolConnectorId(mcpTool.ServerAddress),
                        mcpTool.AuthorizationToken,
                        mcpTool.ServerDescription);

                if (mcpTool.AllowedTools is not null)
                {
                    responsesMcpTool.AllowedTools = new();
                    AddAllMcpFilters(mcpTool.AllowedTools, responsesMcpTool.AllowedTools);
                }

                switch (mcpTool.ApprovalMode)
                {
                    case HostedMcpServerToolAlwaysRequireApprovalMode:
                        responsesMcpTool.ToolCallApprovalPolicy = new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.AlwaysRequireApproval);
                        break;

                    case HostedMcpServerToolNeverRequireApprovalMode:
                        responsesMcpTool.ToolCallApprovalPolicy = new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval);
                        break;

                    case HostedMcpServerToolRequireSpecificApprovalMode specificMode:
                        responsesMcpTool.ToolCallApprovalPolicy = new McpToolCallApprovalPolicy(new CustomMcpToolCallApprovalPolicy());

                        if (specificMode.AlwaysRequireApprovalToolNames is { Count: > 0 } alwaysRequireToolNames)
                        {
                            responsesMcpTool.ToolCallApprovalPolicy.CustomPolicy.ToolsAlwaysRequiringApproval = new();
                            AddAllMcpFilters(alwaysRequireToolNames, responsesMcpTool.ToolCallApprovalPolicy.CustomPolicy.ToolsAlwaysRequiringApproval);
                        }

                        if (specificMode.NeverRequireApprovalToolNames is { Count: > 0 } neverRequireToolNames)
                        {
                            responsesMcpTool.ToolCallApprovalPolicy.CustomPolicy.ToolsNeverRequiringApproval = new();
                            AddAllMcpFilters(neverRequireToolNames, responsesMcpTool.ToolCallApprovalPolicy.CustomPolicy.ToolsNeverRequiringApproval);
                        }

                        break;
                }

                return responsesMcpTool;

            default:
                throw new NotSupportedException($"Tool of type '{tool.GetType().FullName}' is not supported.");
        }
    }

    private static void AddAllMcpFilters(IList<string> toolNames, McpToolFilter filter)
    {
        foreach (var toolName in toolNames)
        {
            filter.ToolNames.Add(toolName);
        }
    }

    /// <summary>Used to create the JSON payload for an OpenAI tool description.</summary>
    internal sealed class ToolJson
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "object";

        [JsonPropertyName("required")]
        public HashSet<string> Required { get; set; } = [];

        [JsonPropertyName("properties")]
        public Dictionary<string, JsonElement> Properties { get; set; } = [];

        [JsonPropertyName("additionalProperties")]
        public bool AdditionalProperties { get; set; }
    }

    #endregion
}
