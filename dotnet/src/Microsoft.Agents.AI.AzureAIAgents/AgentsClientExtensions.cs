// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.AzureAIAgents;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;
using OpenAI;
using OpenAI.Responses;

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Azure.AI.Agents;

/// <summary>
/// Provides extension methods for <see cref="AgentsClient"/>.
/// </summary>
public static class AgentsClientExtensions
{
    /// <summary>
    /// Gets a runnable agent instance from the provided agent record.
    /// </summary>
    /// <param name="agentsClient">The client used to interact with persistent agents. Cannot be <see langword="null"/>.</param>
    /// <param name="model">The model to be used by the agent.</param>
    /// <param name="agentRecord">The agent record to be converted. The latest version will be used. Cannot be <see langword="null"/>.</param>
    /// <param name="chatOptions">The default <see cref="ChatOptions"/> to use when interacting with the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations based on the latest version of the Azure AI Agent.</returns>
    public static ChatClientAgent GetAIAgent(
        this AgentsClient agentsClient,
        string model,
        AgentRecord agentRecord,
        ChatOptions? chatOptions = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
        => GetAIAgent(
            agentsClient,
            model,
            Throw.IfNull(agentRecord).Versions.Latest,
            chatOptions,
            clientFactory,
            openAIClientOptions,
            cancellationToken);

    /// <summary>
    /// Gets a runnable agent instance from the provided agent record.
    /// </summary>
    /// <param name="agentsClient">The client used to interact with persistent agents. Cannot be <see langword="null"/>.</param>
    /// <param name="model">The model to be used by the agent.</param>
    /// <param name="agentVersion">The agent version to be converted. Cannot be <see langword="null"/>.</param>
    /// <param name="chatOptions">The default <see cref="ChatOptions"/> to use when interacting with the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations based on the specified version of the Azure AI Agent.</returns>
    public static ChatClientAgent GetAIAgent(
        this AgentsClient agentsClient,
        string model,
        AgentVersion agentVersion,
        ChatOptions? chatOptions = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNull(model);
        Throw.IfNull(agentVersion);

        if (model is null)
        {
            throw new ArgumentException("When not using a PromptAgent the model needs to be provided in the ChatOptions.ModelId property");
        }

        return GetAIAgent(agentsClient, model, agentVersion, new ChatClientAgentOptions() { ChatOptions = chatOptions }, clientFactory, openAIClientOptions, cancellationToken);
    }

    /// <summary>
    /// Retrieves an existing server side agent, wrapped as a <see cref="ChatClientAgent"/> using the provided <see cref="AgentsClient"/>.
    /// </summary>
    /// <param name="agentsClient">The <see cref="AgentsClient"/> to create the <see cref="ChatClientAgent"/> with.</param>
    /// <returns>A <see cref="ChatClientAgent"/> for the Azure AI Agent.</returns>
    /// <param name="model">The model to be used by the agent.</param>
    /// <param name="name"> The name of the server side agent to create a <see cref="ChatClientAgent"/> for.</param>
    /// <param name="chatOptions">Options that should apply to all runs of the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations based on the latest version of the named Azure AI Agent.</returns>
    public static ChatClientAgent GetAIAgent(
        this AgentsClient agentsClient,
        string model,
        string name,
        ChatOptions? chatOptions = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNullOrWhitespace(model);
        Throw.IfNullOrWhitespace(name);

        var agentRecord = agentsClient.GetAgent(name, cancellationToken);
        if (agentRecord is null)
        {
            throw new InvalidOperationException($"Agent with name '{name}' not found.");
        }

        return GetAIAgent(agentsClient, model, agentRecord, chatOptions, clientFactory, openAIClientOptions, cancellationToken);
    }

    /// <summary>
    /// Retrieves an existing server side agent, wrapped as a <see cref="ChatClientAgent"/> using the provided <see cref="AgentsClient"/>.
    /// </summary>
    /// <param name="agentsClient">The <see cref="AgentsClient"/> to create the <see cref="ChatClientAgent"/> with.</param>
    /// <returns>A <see cref="ChatClientAgent"/> for the persistent agent.</returns>
    /// <param name="model">The model to be used by the agent.</param>
    /// <param name="name"> The name of the server side agent to create a <see cref="ChatClientAgent"/> for.</param>
    /// <param name="chatOptions">Options that should apply to all runs of the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations based on the latest version of the named Azure AI Agent.</returns>
    public static async Task<ChatClientAgent> GetAIAgentAsync(
        this AgentsClient agentsClient,
        string model,
        string name,
        ChatOptions? chatOptions = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNullOrWhitespace(model);
        Throw.IfNullOrWhitespace(name);

        var agentRecord = await agentsClient.GetAgentAsync(name, cancellationToken).ConfigureAwait(false);
        if (agentRecord is null)
        {
            throw new InvalidOperationException($"Agent with name '{name}' not found.");
        }

        return GetAIAgent(agentsClient, model, agentRecord, chatOptions, clientFactory, openAIClientOptions, cancellationToken);
    }

    /// <summary>
    /// Gets a runnable agent instance from a <see cref="AgentVersion"/> containing metadata about an Azure AI Agent.
    /// </summary>
    /// <param name="agentsClient">The client used to interact with persistent agents. Cannot be <see langword="null"/>.</param>
    /// <param name="model">The model to be used by the agent.</param>
    /// <param name="agentRecord">The agent record to be converted. The latest version will be used. Cannot be <see langword="null"/>.</param>
    /// <param name="options">Full set of options to configure the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations based on the latest version of the Azure AI Agent record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentsClient"/>, <paramref name="model"/>, <paramref name="agentRecord"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static ChatClientAgent GetAIAgent(
        this AgentsClient agentsClient,
        string model,
        AgentRecord agentRecord,
        ChatClientAgentOptions? options = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
        => GetAIAgent(
            agentsClient,
            model,
            agentRecord.Versions.Latest,
            options ?? new ChatClientAgentOptions(),
            clientFactory,
            openAIClientOptions,
            cancellationToken);

    /// <summary>
    /// Gets a runnable agent instance from a <see cref="AgentVersion"/> containing metadata about an Azure AI Agent.
    /// </summary>
    /// <param name="agentsClient">The client used to interact with persistent agents. Cannot be <see langword="null"/>.</param>
    /// <param name="model">The model to be used by the agent.</param>
    /// <param name="agentVersion">The agent version to be converted. Cannot be <see langword="null"/>.</param>
    /// <param name="options">Full set of options to configure the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations based on the provided version of the Azure AI Agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentsClient"/>, <paramref name="model"/>, <paramref name="agentRecord"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static ChatClientAgent GetAIAgent(
        this AgentsClient agentsClient,
        string model,
        AgentVersion agentVersion,
        ChatClientAgentOptions? options = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNullOrWhitespace(model);
        Throw.IfNull(agentVersion);
        Throw.IfNull(options);

        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentVersion, model, openAIClientOptions);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        ChatClientAgentOptions? agentOptions;

        // If options are null, populate from agentRecord definition
        var version = agentVersion;

        if (options is null)
        {
            agentOptions = new();
            agentOptions.Id = GetAgentId(agentVersion);
            agentOptions.Name = agentVersion.Name;

            agentOptions.Description = version.Description;

            if (version.Definition is PromptAgentDefinition promptDef && promptDef.Tools is { Count: > 0 })
            {
                agentOptions.ChatOptions = new ChatOptions();
                agentOptions.ChatOptions.Tools = [];
                agentOptions.Instructions = promptDef.Instructions;

                foreach (var tool in promptDef.Tools)
                {
                    agentOptions.ChatOptions.Tools.Add(tool);
                }
            }
        }
        else
        {
            // When agent options it is used when available otherwise fallback to the agent definition used for the agent record.
            agentOptions = new ChatClientAgentOptions()
            {
                Id = options.Id ?? GetAgentId(agentVersion),
                Name = options.Name ?? agentVersion.Name,
                Description = options.Description ?? version.Description,
                Instructions = options.Instructions ?? options.ChatOptions?.Instructions ?? (version.Definition as PromptAgentDefinition)?.Instructions,
                ChatOptions = options.ChatOptions,
                AIContextProviderFactory = options.AIContextProviderFactory,
                ChatMessageStoreFactory = options.ChatMessageStoreFactory,
                UseProvidedChatClientAsIs = options.UseProvidedChatClientAsIs
            };

            // If no tools were provided in options, but exist in the agent definition, use those.
            if (agentOptions.ChatOptions?.Tools is null or { Count: 0 } && version.Definition is PromptAgentDefinition promptDef && promptDef.Tools is { Count: > 0 })
            {
                agentOptions.ChatOptions ??= new ChatOptions();
                agentOptions.ChatOptions.Tools ??= [];

                foreach (var tool in promptDef.Tools)
                {
                    agentOptions.ChatOptions.Tools.Add(tool);
                }
            }
        }

        return new ChatClientAgent(chatClient, agentOptions);
    }

    /// <summary>
    /// Retrieves an existing server side agent, wrapped as a <see cref="ChatClientAgent"/> using the provided <see cref="AgentsClient"/>.
    /// </summary>
    /// <param name="agentsClient">The <see cref="AgentsClient"/> to create the <see cref="ChatClientAgent"/> with.</param>
    /// <param name="model">The model to be used by the agent.</param>
    /// <param name="name">The ID of the server side agent to create a <see cref="ChatClientAgent"/> for.</param>
    /// <param name="options">Full set of options to configure the agent.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations based on the latest version of named the Azure AI Agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentsClient"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty or whitespace.</exception>
    public static async Task<ChatClientAgent> GetAIAgentAsync(
        this AgentsClient agentsClient,
        string model,
        string name,
        ChatClientAgentOptions options,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNullOrWhitespace(name);
        Throw.IfNull(options);

        var agentRecord = await agentsClient.GetAgentAsync(name, cancellationToken).ConfigureAwait(false);
        return agentsClient.GetAIAgent(model, agentRecord, options, clientFactory, openAIClientOptions, cancellationToken);
    }

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
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
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
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNull(model);

        var (promptAgentDefinition, versionCreationOptions) = CreatePromptAgentDefinitionAndOptions(
            model, instructions, temperature, topP, raiConfig, reasoningOptions, textOptions, tools, structuredInputs, metadata);

        AgentVersion agentVersion = agentsClient.CreateAgentVersion(name, promptAgentDefinition, versionCreationOptions, cancellationToken);
        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentVersion, model, openAIClientOptions);

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
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
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
        OpenAIClientOptions? openAIClientOptions = null,
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
        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentRecord, model, openAIClientOptions);

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
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the operation if needed.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the newly created agent.</returns>
    /// <exception cref="ArgumentException">Thrown if neither the 'model' parameter nor a model in the agent definition is provided.</exception>
    public static ChatClientAgent CreateAIAgent(
        this AgentsClient agentsClient,
        string model,
        ChatClientAgentOptions options,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
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

        AgentVersion agentVersion = agentsClient.CreateAgentVersion(options.Name, promptAgentDefinition, new() { Description = options.Description }, cancellationToken);
        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentVersion, model, openAIClientOptions);

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
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the newly created agent.</returns>
    /// <exception cref="ArgumentException">Thrown if neither the 'model' parameter nor a model in the agent definition is provided.</exception>
    public static async Task<ChatClientAgent> CreateAIAgentAsync(
        this AgentsClient agentsClient,
        AgentDefinition agentDefinition,
        string? model = null,
        string? name = null,
        AgentCreationOptions? creationOptions = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
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
        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentRecord, model, openAIClientOptions);

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
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the newly created agent.</returns>
    public static async Task<ChatClientAgent> CreateAIAgentAsync(
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
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNull(model);

        var (promptAgentDefinition, versionCreationOptions) = CreatePromptAgentDefinitionAndOptions(
            model, instructions, temperature, topP, raiConfig, reasoningOptions, textOptions, tools, structuredInputs, metadata);

        AgentVersion agentVersion = await agentsClient.CreateAgentVersionAsync(name, promptAgentDefinition, versionCreationOptions, cancellationToken).ConfigureAwait(false);
        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentVersion, model, openAIClientOptions);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        return new ChatClientAgent(chatClient);
    }

    #region Private

    private static (PromptAgentDefinition, AgentVersionCreationOptions?) CreatePromptAgentDefinitionAndOptions(
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

        AgentVersionCreationOptions? creationOptions = null;
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

    private static string GetAgentId(AgentVersion agentVersion)
        => $"{agentVersion.Name}:{agentVersion.Id}";

    #endregion

    #region Polyfill from MEAI.OpenAI for AITool -> ResponseTool conversion

    // This code will be removed and replaced by the utility tool made public in the PR below for Microsoft.Extensions.AI.OpenAI package
    // PR https://github.com/dotnet/extensions/pull/6958

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
