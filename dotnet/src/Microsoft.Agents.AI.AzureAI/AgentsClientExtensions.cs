// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.AzureAI;
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
    /// <param name="agentsClient">The client used to interact with Azure AI Agents. Cannot be <see langword="null"/>.</param>
    /// <param name="agentRecord">The agent record to be converted. The latest version will be used. Cannot be <see langword="null"/>.</param>
    /// <param name="tools">The tools to use when interacting with the agent. This is required when using prompt agent definitions with tools.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations based on the latest version of the Azure AI Agent.</returns>
    /// <remarks>When using prompt agent definitions with tools the parameter <paramref name="tools"/> needs to be provided.</remarks>
    public static ChatClientAgent GetAIAgent(
        this AgentsClient agentsClient,
        AgentRecord agentRecord,
        IList<AITool>? tools = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNull(agentRecord);

        return GetAIAgent(agentsClient, agentRecord.Versions.Latest, tools, clientFactory, openAIClientOptions, cancellationToken);
    }

    /// <summary>
    /// Retrieves an existing server side agent, wrapped as a <see cref="ChatClientAgent"/> using the provided <see cref="AgentsClient"/>.
    /// </summary>
    /// <param name="agentsClient">The <see cref="AgentsClient"/> to create the <see cref="ChatClientAgent"/> with. Cannot be <see langword="null"/>.</param>
    /// <param name="name">The name of the server side agent to create a <see cref="ChatClientAgent"/> for. Cannot be <see langword="null"/> or whitespace.</param>
    /// <param name="tools">The tools to use when interacting with the agent. This is required when using prompt agent definitions with tools.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations based on the latest version of the named Azure AI Agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentsClient"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty or whitespace, or when the agent with the specified name was not found.</exception>
    /// <exception cref="InvalidOperationException">The agent with the specified name was not found.</exception>
    /// <remarks>When using prompt agent definitions with tools the parameter <paramref name="tools"/> needs to be provided.</remarks>
    public static ChatClientAgent GetAIAgent(
        this AgentsClient agentsClient,
        string name,
        IList<AITool>? tools = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNullOrWhitespace(name);

        var agentRecord = agentsClient.GetAgent(name, cancellationToken).Value
            ?? throw new InvalidOperationException($"Agent with name '{name}' not found.");

        return GetAIAgent(agentsClient, agentRecord, tools, clientFactory, openAIClientOptions, cancellationToken);
    }

    /// <summary>
    /// Asynchronously retrieves an existing server side agent, wrapped as a <see cref="ChatClientAgent"/> using the provided <see cref="AgentsClient"/>.
    /// </summary>
    /// <param name="agentsClient">The <see cref="AgentsClient"/> to create the <see cref="ChatClientAgent"/> with. Cannot be <see langword="null"/>.</param>
    /// <param name="name">The name of the server side agent to create a <see cref="ChatClientAgent"/> for. Cannot be <see langword="null"/> or whitespace.</param>
    /// <param name="tools">The tools to use when interacting with the agent. This is required when using prompt agent definitions with tools.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations based on the latest version of the named Azure AI Agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentsClient"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty or whitespace, or when the agent with the specified name was not found.</exception>
    /// <exception cref="InvalidOperationException">The agent with the specified name was not found.</exception>
    /// <remarks>
    /// When created agents are defined with tools the <paramref name="tools"/> needs to be provided.
    /// <para>Attempting to provide less tools that are defined in the agent definition will result in an error when the agent is executed.</para>
    /// <para>Providing more tools than defined in the agent definition will result in those to being ignored by the agent during execution.</para>
    /// </remarks>
    /// <remarks>When using prompt agent definitions with tools the parameter <paramref name="tools"/> needs to be provided.</remarks>
    public static async Task<ChatClientAgent> GetAIAgentAsync(
        this AgentsClient agentsClient,
        string name,
        IList<AITool>? tools = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNullOrWhitespace(name);

        var agentRecord = (await agentsClient.GetAgentAsync(name, cancellationToken).ConfigureAwait(false)).Value
            ?? throw new InvalidOperationException($"Agent with name '{name}' not found.");

        return GetAIAgent(agentsClient, agentRecord, tools, clientFactory, openAIClientOptions, cancellationToken);
    }

    /// <summary>
    /// Gets a runnable agent instance from a <see cref="AgentVersion"/> containing metadata about an Azure AI Agent.
    /// </summary>
    /// <param name="agentsClient">The client used to interact with Azure AI Agents. Cannot be <see langword="null"/>.</param>
    /// <param name="agentVersion">The agent version to be converted. Cannot be <see langword="null"/>.</param>
    /// <param name="tools">The tools to use when interacting with the agent. This is required when using prompt agent definitions with tools.</param>
    /// <param name="clientFactory">Provides a way to customize the creation of the underlying <see cref="IChatClient"/> used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations based on the provided version of the Azure AI Agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentsClient"/> or <paramref name="agentVersion"/> is <see langword="null"/>.</exception>
    /// <remarks>When using prompt agent definitions with tools the parameter <paramref name="tools"/> needs to be provided.</remarks>
    public static ChatClientAgent GetAIAgent(
        this AgentsClient agentsClient,
        AgentVersion agentVersion,
        IList<AITool>? tools = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNull(agentVersion);

        ValidateToolsToAgentDefinition(agentVersion.Definition, tools);

        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentVersion, tools, openAIClientOptions);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        var agentOptions = new ChatClientAgentOptions()
        {
            Id = agentVersion.Id,
            Name = agentVersion.Name,
            Description = agentVersion.Description,
        };

        if (agentVersion.Definition is PromptAgentDefinition promptAgentDefinition)
        {
            agentOptions.Instructions = promptAgentDefinition.Instructions;
            agentOptions.ChatOptions = new()
            {
                Temperature = promptAgentDefinition.Temperature,
                TopP = promptAgentDefinition.TopP,
                Instructions = promptAgentDefinition.Instructions,
            };
        }

        if (tools is { Count: > 0 } aiTools)
        {
            agentOptions.ChatOptions ??= new ChatOptions();
            agentOptions.ChatOptions.Tools = aiTools;
        }

        return new ChatClientAgent(chatClient, agentOptions);
    }

    /// <summary>
    /// Creates a new AI agent using the specified agent definition and optional configuration parameters.
    /// </summary>
    /// <param name="agentsClient">The client used to manage and interact with AI agents. Cannot be <see langword="null"/>.</param>
    /// <param name="name">The name for the agent.</param>
    /// <param name="agentDefinition">The definition that specifies the configuration and behavior of the agent to create. Cannot be <see langword="null"/>.</param>
    /// <param name="tools">The tools to use when interacting with the agent, this is required when using prompt agent definitions with tools.</param>
    /// <param name="creationOptions">Settings that control the creation of the agent.</param>
    /// <param name="clientFactory">A factory function to customize the creation of the chat client used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the newly created agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentsClient"/> or <paramref name="agentDefinition"/> is <see langword="null"/>.</exception>
    /// <remarks>When using prompt agent definitions with tools the parameter <paramref name="tools"/> needs to be provided.</remarks>
    public static ChatClientAgent CreateAIAgent(
        this AgentsClient agentsClient,
        string name,
        AgentDefinition agentDefinition,
        IList<AITool>? tools = null,
        AgentCreationOptions? creationOptions = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNullOrWhitespace(name);
        Throw.IfNull(agentDefinition);

        ValidateToolsToAgentDefinition(agentDefinition, tools);
        ApplyToolsToAgentDefinition(agentDefinition, tools);

        AgentRecord agentRecord = agentsClient.CreateAgent(name, agentDefinition, creationOptions, cancellationToken);
        AgentVersion agentVersion = agentRecord.Versions.Latest;
        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentVersion, tools, openAIClientOptions);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        var agentOptions = new ChatClientAgentOptions()
        {
            Id = agentVersion.Id,
            Name = agentVersion.Name,
            Description = agentVersion.Description,
        };

        if (agentDefinition is PromptAgentDefinition promptAgentDefinition)
        {
            agentOptions.Instructions = promptAgentDefinition.Instructions;
            agentOptions.ChatOptions = new()
            {
                Temperature = promptAgentDefinition.Temperature,
                TopP = promptAgentDefinition.TopP,
                Instructions = promptAgentDefinition.Instructions,
            };
        }

        if (tools is { Count: > 0 } aiTools)
        {
            agentOptions.ChatOptions ??= new ChatOptions();
            agentOptions.ChatOptions.Tools = aiTools;
        }

        return new ChatClientAgent(chatClient, agentOptions);
    }

    /// <summary>
    /// Creates a new Prompt AI agent using the specified configuration parameters.
    /// </summary>
    /// <param name="agentsClient">The client used to manage and interact with AI agents. Cannot be <see langword="null"/>.</param>
    /// <param name="name">The name for the agent.</param>
    /// <param name="model">The name of the model to use for the agent. Cannot be <see langword="null"/> or whitespace.</param>
    /// <param name="instructions">The instructions that guide the agent's behavior. Cannot be <see langword="null"/> or whitespace.</param>
    /// <param name="tools">The tools to use when interacting with the agent, this is required when using prompt agent definitions with tools.</param>
    /// <param name="creationOptions">Settings that control the creation of the agent.</param>
    /// <param name="clientFactory">A factory function to customize the creation of the chat client used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the newly created agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentsClient"/>, <paramref name="model"/>, or <paramref name="instructions"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="model"/> or <paramref name="instructions"/> is empty or whitespace.</exception>"
    /// <remarks>When using prompt agent definitions with tools the parameter <paramref name="tools"/> needs to be provided.</remarks>
    public static ChatClientAgent CreateAIAgent(
        this AgentsClient agentsClient,
        string name,
        string model,
        string instructions,
        IList<AITool>? tools = null,
        AgentCreationOptions? creationOptions = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNullOrWhitespace(name);
        Throw.IfNullOrWhitespace(model);
        Throw.IfNullOrWhitespace(instructions);

        return CreateAIAgent(agentsClient, name, new PromptAgentDefinition(model) { Instructions = instructions }, tools, creationOptions, clientFactory, openAIClientOptions, cancellationToken);
    }

    /// <summary>
    /// Creates a new Prompt AI agent using the specified configuration parameters.
    /// </summary>
    /// <param name="agentsClient">The client used to manage and interact with AI agents. Cannot be <see langword="null"/>.</param>
    /// <param name="name">The name for the agent.</param>
    /// <param name="model">The name of the model to use for the agent. Cannot be <see langword="null"/> or whitespace.</param>
    /// <param name="instructions">The instructions that guide the agent's behavior. Cannot be <see langword="null"/> or whitespace.</param>
    /// <param name="tools">The tools to use when interacting with the agent, this is required when using prompt agent definitions with tools.</param>
    /// <param name="creationOptions">Settings that control the creation of the agent.</param>
    /// <param name="clientFactory">A factory function to customize the creation of the chat client used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the newly created agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentsClient"/>, <paramref name="model"/>, or <paramref name="instructions"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="model"/> or <paramref name="instructions"/> is empty or whitespace.</exception>"
    /// <remarks>When using prompt agent definitions with tools the parameter <paramref name="tools"/> needs to be provided.</remarks>
    public static Task<ChatClientAgent> CreateAIAgentAsync(
        this AgentsClient agentsClient,
        string name,
        string model,
        string instructions,
        IList<AITool>? tools = null,
        AgentVersionCreationOptions? creationOptions = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNullOrWhitespace(name);
        Throw.IfNullOrWhitespace(model);
        Throw.IfNullOrWhitespace(instructions);

        return CreateAIAgentAsync(agentsClient, name, new PromptAgentDefinition(model) { Instructions = instructions }, tools, creationOptions, clientFactory, openAIClientOptions, cancellationToken);
    }

    /// <summary>
    /// Creates a new Prompt AI Agent using the provided <see cref="AgentsClient"/> and options.
    /// </summary>
    /// <param name="agentsClient">The client used to manage and interact with AI agents. Cannot be <see langword="null"/>.</param>
    /// <param name="model">The name of the model to use for the agent. Cannot be <see langword="null"/> or whitespace.</param>
    /// <param name="options">The options for creating the agent. Cannot be <see langword="null"/>.</param>
    /// <param name="clientFactory">A factory function to customize the creation of the chat client used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the operation if needed.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the newly created agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentsClient"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="model"/> is empty or whitespace, or when the agent name is not provided in the options.</exception>
    public static ChatClientAgent CreateAIAgent(
        this AgentsClient agentsClient,
        string model,
        ChatClientAgentOptions options,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(agentsClient);
        Throw.IfNull(options);
        Throw.IfNullOrWhitespace(model);

        if (string.IsNullOrWhiteSpace(options.Name))
        {
            throw new ArgumentException("Agent name must be provided in the options.Name property", nameof(options));
        }

        PromptAgentDefinition agentDefinition = new(model)
        {
            Model = model,
            Instructions = options.Instructions,
        };

        var chatOptions = new ChatOptions()
        {
            Instructions = options.Instructions,
        };

        AgentVersionCreationOptions? versionCreationOptions = null;
        if (!string.IsNullOrWhiteSpace(options.Description))
        {
            (versionCreationOptions ??= new()).Description = options.Description;
        }

        var chatClientAgentOptions = new ChatClientAgentOptions()
        {
            Name = options.Name,
            Instructions = options.Instructions,
            Description = options.Description,
            ChatOptions = chatOptions
        };

        if (options.ChatOptions?.Tools is { Count: > 0 })
        {
            foreach (var tool in options.ChatOptions.Tools)
            {
                agentDefinition.Tools.Add(ToResponseTool(tool, options.ChatOptions));
            }
        }

        AgentVersion agentVersion = agentsClient.CreateAgentVersion(options.Name, agentDefinition, versionCreationOptions, cancellationToken).Value;

        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentVersion, options.ChatOptions?.Tools, openAIClientOptions);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        chatClientAgentOptions.Id = agentVersion.Id;

        return new ChatClientAgent(chatClient, chatClientAgentOptions);
    }

    /// <summary>
    /// Asynchronously creates a new AI agent using the specified agent definition and optional configuration
    /// parameters.
    /// </summary>
    /// <param name="agentsClient">The client used to manage and interact with AI agents. Cannot be <see langword="null"/>.</param>
    /// <param name="name">The name for the agent.</param>
    /// <param name="agentDefinition">The definition that specifies the configuration and behavior of the agent to create. Cannot be <see langword="null"/>.</param>
    /// <param name="tools">The tools to use when interacting with the agent. This is required when using prompt agent definitions with tools.</param>
    /// <param name="agentVersionCreationOptions">Settings that control the creation of the agent.</param>
    /// <param name="clientFactory">A factory function to customize the creation of the chat client used by the agent.</param>
    /// <param name="openAIClientOptions">An optional <see cref="OpenAIClientOptions"/> for configuring the underlying OpenAI client.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance that can be used to perform operations on the newly created agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentsClient"/> or <paramref name="agentDefinition"/> is <see langword="null"/>.</exception>
    /// <remarks>When using prompt agent definitions with tools the parameter <paramref name="tools"/> needs to be provided.</remarks>
    public static async Task<ChatClientAgent> CreateAIAgentAsync(
        this AgentsClient agentsClient,
        string name,
        AgentDefinition agentDefinition,
        IList<AITool>? tools = null,
        AgentVersionCreationOptions? agentVersionCreationOptions = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        OpenAIClientOptions? openAIClientOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhitespace(name);
        Throw.IfNull(agentsClient);
        Throw.IfNull(agentDefinition);

        ValidateToolsToAgentDefinition(agentDefinition, tools);
        ApplyToolsToAgentDefinition(agentDefinition, tools);

        AgentVersion agentVersion = await agentsClient.CreateAgentVersionAsync(name, agentDefinition, agentVersionCreationOptions, cancellationToken).ConfigureAwait(false);
        IChatClient chatClient = new AzureAIAgentChatClient(agentsClient, agentVersion, tools, openAIClientOptions);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        List<AITool>? aiTools = null;
        if (agentDefinition is PromptAgentDefinition { Tools: { Count: > 0 } definitionTools })
        {
            aiTools = definitionTools.Select(rt => rt.AsAITool()).ToList();
        }

        return new ChatClientAgent(chatClient, new ChatClientAgentOptions()
        {
            Id = agentVersion.Id,
            Name = name,
            Description = agentVersionCreationOptions?.Description,
            Instructions = (agentDefinition as PromptAgentDefinition)?.Instructions,
            ChatOptions = new ChatOptions()
            {
                Tools = aiTools
            }
        });
    }

    #region Private

    private static void ValidateToolsToAgentDefinition(AgentDefinition agentDefinition, IList<AITool>? tools)
    {
        // If the agent definition contains tools, ensure they are provided via tools parameter.
        // This check ensures that the tools are executable when the agent is run.
        if ((agentDefinition as PromptAgentDefinition)?.Tools is { Count: > 0 })
        {
            throw new ArgumentException("When creating prompt agent definitions use the dedicated tools parameter to provide the necessary tools.", nameof(tools));
        }
    }

    private static void ApplyToolsToAgentDefinition(AgentDefinition agentDefinition, IList<AITool>? tools)
    {
        if (tools is { Count: > 0 })
        {
            if (agentDefinition is not PromptAgentDefinition promptAgentDefinition)
            {
                throw new ArgumentException("Only prompt agent definitions support tools.", nameof(agentDefinition));
            }

            foreach (var tool in tools)
            {
                // Ensure that any AIFunctions provided are In-Proc ready, not just the declarations.
                if (tool is AIFunctionDeclaration and not AIFunction)
                {
                    throw new InvalidOperationException("When providing function avoid converting FunctionTools to AITools, use AIFunctionFactory instead.");
                }

                // If this is a converted ResponseTool as AITool, we can directly retrieve the ResponseTool instance from GetService.
                promptAgentDefinition.Tools.Add(tool.GetService<ResponseTool>() ?? ToResponseTool(tool));
            }
        }
    }

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

    private static ResponseTool ToResponseTool(AITool tool, ChatOptions? options = null)
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
