// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace Microsoft.Agents.AI.Foundry;

/// <summary>
/// Internal Foundry chat-client decorator that unifies the three Foundry chat-client construction
/// modes (pure responses, server-side agent reference, hosted agent endpoint) behind a single
/// type and centralises Foundry-specific concerns: <c>microsoft.foundry</c> telemetry tagging,
/// <c>agent-framework-dotnet/{version}</c> User-Agent stamping, and (for server-side agents)
/// per-request payload mutation that injects the agent reference and strips per-request
/// overrides that the server owns.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the previous <c>AzureAIProjectChatClient</c> and <c>AzureAIProjectResponsesChatClient</c>
/// decorators. All Foundry entry points (the public <c>FoundryAgent</c> constructors and the
/// <c>AIProjectClientExtensions.AsAIAgent</c> overloads) now construct a
/// <see cref="FoundryChatClient"/> internally, so telemetry and the agent-framework User-Agent
/// segment are uniform across paths.
/// </para>
/// <para>
/// The type is intentionally <see langword="internal"/> for now. The public surface remains the
/// existing <c>FoundryAgent</c> and <c>AsAIAgent</c> shapes; promotion is deferred until external
/// callers express need.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
internal sealed class FoundryChatClient : DelegatingChatClient
{
    private readonly ChatClientMetadata _metadata;
    private readonly AIProjectClient? _aiProjectClient;
    private readonly ProjectOpenAIClient _projectOpenAIClient;
    private readonly AgentReference? _agentReference;
    private readonly ProjectsAgentVersion? _agentVersion;
    private readonly ProjectsAgentRecord? _agentRecord;
    private readonly ChatOptions? _baseChatOptions;

    /// <summary>
    /// Initializes a new instance for mode 1: pure responses via a project-level model id.
    /// </summary>
    /// <param name="aiProjectClient">The project client.</param>
    /// <param name="modelId">The model deployment id.</param>
    internal FoundryChatClient(AIProjectClient aiProjectClient, string modelId)
        : base(Throw.IfNull(aiProjectClient)
            .GetProjectOpenAIClient()
            .GetProjectResponsesClientForModel(Throw.IfNullOrWhitespace(modelId))
            .AsIChatClient())
    {
        this._aiProjectClient = aiProjectClient;
        this._projectOpenAIClient = aiProjectClient.GetProjectOpenAIClient();
        this._metadata = new ChatClientMetadata("microsoft.foundry", defaultModelId: modelId);
        TryRegisterAgentFrameworkUserAgentPolicy(this.InnerClient);
    }

    /// <summary>
    /// Initializes a new instance for mode 2 (root): server-side agent invoked by reference.
    /// </summary>
    internal FoundryChatClient(AIProjectClient aiProjectClient, AgentReference agentReference, string? defaultModelId, ChatOptions? baseChatOptions)
        : base(Throw.IfNull(aiProjectClient)
            .GetProjectOpenAIClient()
            .GetProjectResponsesClientForAgent(Throw.IfNull(agentReference))
            .AsIChatClient())
    {
        this._aiProjectClient = aiProjectClient;
        this._agentReference = agentReference;
        this._projectOpenAIClient = aiProjectClient.GetProjectOpenAIClient();
        this._metadata = new ChatClientMetadata("microsoft.foundry", defaultModelId: defaultModelId);
        this._baseChatOptions = baseChatOptions;
        TryRegisterAgentFrameworkUserAgentPolicy(this.InnerClient);
    }

    /// <summary>
    /// Initializes a new instance for mode 2 (record variant): server-side agent invoked by
    /// record, resolving to the latest version.
    /// </summary>
    internal FoundryChatClient(AIProjectClient aiProjectClient, ProjectsAgentRecord agentRecord, ChatOptions? baseChatOptions)
        : this(aiProjectClient, Throw.IfNull(agentRecord).GetLatestVersion(), baseChatOptions)
    {
        this._agentRecord = agentRecord;
    }

    /// <summary>
    /// Initializes a new instance for mode 2 (version variant): server-side agent invoked by
    /// a specific version.
    /// </summary>
    internal FoundryChatClient(AIProjectClient aiProjectClient, ProjectsAgentVersion agentVersion, ChatOptions? baseChatOptions)
        : this(
              aiProjectClient,
              CreateAgentReference(Throw.IfNull(agentVersion)),
              (agentVersion.Definition as DeclarativeAgentDefinition)?.Model,
              baseChatOptions)
    {
        this._agentVersion = agentVersion;
    }

    /// <summary>
    /// Initializes a new instance for mode 3: hosted agent endpoint URL. Parses the URL into
    /// its per-agent <see cref="ProjectOpenAIClient"/> shape internally and forwards through
    /// the resulting responses client.
    /// </summary>
    /// <param name="agentEndpoint">
    /// The agent-specific endpoint URI. Must be of the shape
    /// <c>https://&lt;host&gt;/.../projects/&lt;project&gt;/agents/&lt;agentName&gt;/endpoint/protocols/openai</c>.
    /// </param>
    /// <param name="credential">The authentication credential.</param>
    /// <param name="clientOptions">Optional per-agent client options. <c>Endpoint</c> and <c>AgentName</c> are owned by this ctor and overridden with values derived from <paramref name="agentEndpoint"/>.</param>
    internal FoundryChatClient(Uri agentEndpoint, AuthenticationTokenProvider credential, ProjectOpenAIClientOptions? clientOptions)
        : this(BuildHostedAgentEndpointInner(agentEndpoint, credential, clientOptions))
    {
    }

    private FoundryChatClient(HostedAgentEndpointInner inner)
        : base(inner.ChatClient)
    {
        this._projectOpenAIClient = inner.PerAgentClient;
        this.HostedAgentName = inner.AgentName;
        this._metadata = new ChatClientMetadata("microsoft.foundry");
        TryRegisterAgentFrameworkUserAgentPolicy(this.InnerClient);
    }

    /// <summary>
    /// Gets the hosted-agent name parsed from the agent endpoint URL when the chat client was
    /// constructed in mode 3, or <see langword="null"/> for modes 1 and 2.
    /// </summary>
    internal string? HostedAgentName { get; }

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        return (serviceKey is null && serviceType == typeof(ChatClientMetadata))
            ? this._metadata
            : (serviceKey is null && serviceType == typeof(AIProjectClient))
            ? this._aiProjectClient
            : (serviceKey is null && serviceType == typeof(ProjectOpenAIClient))
            ? this._projectOpenAIClient
            : (serviceKey is null && serviceType == typeof(AgentReference))
            ? this._agentReference
            : (serviceKey is null && serviceType == typeof(ProjectsAgentVersion))
            ? this._agentVersion
            : (serviceKey is null && serviceType == typeof(ProjectsAgentRecord))
            ? this._agentRecord
            : base.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var effectiveOptions = this._agentReference is not null
            ? this.GetAgentEnabledChatOptions(options)
            : options;

        return await base.GetResponseAsync(messages, effectiveOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var effectiveOptions = this._agentReference is not null
            ? this.GetAgentEnabledChatOptions(options)
            : options;

        await foreach (var chunk in base.GetStreamingResponseAsync(messages, effectiveOptions, cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Parses an agent endpoint URI of shape
    /// <c>https://&lt;host&gt;/.../projects/&lt;project&gt;/agents/&lt;agentName&gt;/endpoint/protocols/openai</c>
    /// and returns the agent name and the derived project-root URI.
    /// </summary>
    /// <remarks>
    /// Tolerates trailing slash, casing variants on <c>/agents/</c> and the suffix segment, and
    /// strips query string and fragment. Throws <see cref="ArgumentException"/> for inputs that
    /// do not match the expected shape.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The endpoint is missing the <c>/agents/</c> segment, has an empty agent name, or has a
    /// suffix other than <c>/endpoint/protocols/openai</c>.
    /// </exception>
    internal static (string AgentName, Uri ProjectRoot) ParseAgentEndpoint(Uri agentEndpoint)
    {
        Throw.IfNull(agentEndpoint);

        const string AgentsSegment = "/agents/";
        const string ExpectedSuffix = "/endpoint/protocols/openai";

        var path = agentEndpoint.AbsolutePath.TrimEnd('/');
        var idx = path.IndexOf(AgentsSegment, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            throw new ArgumentException(
                $"Expected an agent endpoint of shape 'https://<host>/.../projects/<project>/agents/<agentName>/endpoint/protocols/openai' but got '{agentEndpoint}'. " +
                "If you want to construct a FoundryAgent against a project endpoint, use the (Uri projectEndpoint, AuthenticationTokenProvider credential, string model, string instructions, ...) constructor instead.",
                nameof(agentEndpoint));
        }

        var afterAgents = path.Substring(idx + AgentsSegment.Length);
        var nextSlash = afterAgents.IndexOf('/');
        if (nextSlash <= 0)
        {
            throw new ArgumentException(
                $"Agent endpoint '{agentEndpoint}' is missing the '<agentName>{ExpectedSuffix}' suffix.",
                nameof(agentEndpoint));
        }

        var agentName = afterAgents.Substring(0, nextSlash);
        var suffix = afterAgents.Substring(nextSlash);
        if (!string.Equals(suffix, ExpectedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Agent endpoint '{agentEndpoint}' has an unexpected suffix '{suffix}'. Expected '{ExpectedSuffix}'.",
                nameof(agentEndpoint));
        }

        var rootPath = path.Substring(0, idx);
        var projectRoot = new UriBuilder(agentEndpoint)
        {
            Path = rootPath,
            Query = string.Empty,
            Fragment = string.Empty,
        }.Uri;

        return (agentName, projectRoot);
    }

    /// <summary>
    /// Builds a project-level <see cref="ProjectOpenAIClient"/> for the hosted-agent endpoint
    /// scenario (used by <c>FoundryAgent.CreateConversationSessionAsync</c>). Only copies the
    /// observable primitive properties from a caller-supplied <see cref="ProjectOpenAIClientOptions"/>
    /// because <see cref="ClientPipelineOptions"/> does not publicly enumerate its policies.
    /// </summary>
    internal static ProjectOpenAIClient CreateProjectLevelOpenAIClientFromAgentEndpoint(
        Uri agentEndpoint,
        AuthenticationTokenProvider credential,
        ProjectOpenAIClientOptions? clientOptions)
    {
        var (_, projectRoot) = ParseAgentEndpoint(agentEndpoint);

        var projectOptions = new ProjectOpenAIClientOptions();
        if (clientOptions is not null)
        {
            if (clientOptions.RetryPolicy is not null)
            {
                projectOptions.RetryPolicy = clientOptions.RetryPolicy;
            }

            if (clientOptions.NetworkTimeout is not null)
            {
                projectOptions.NetworkTimeout = clientOptions.NetworkTimeout;
            }

            if (clientOptions.Transport is not null)
            {
                projectOptions.Transport = clientOptions.Transport;
            }

            if (!string.IsNullOrEmpty(clientOptions.UserAgentApplicationId))
            {
                projectOptions.UserAgentApplicationId = clientOptions.UserAgentApplicationId;
            }
        }

        return new ProjectOpenAIClient(projectRoot, credential, projectOptions);
    }

    private ChatOptions GetAgentEnabledChatOptions(ChatOptions? options)
    {
        // Start with a clone of the base chat options defined for the agent, if any.
        ChatOptions agentEnabledChatOptions = this._baseChatOptions?.Clone() ?? new();

        // Ignore per-request all options that can't be overridden.
        agentEnabledChatOptions.Instructions = null;
        agentEnabledChatOptions.Tools = null;
        agentEnabledChatOptions.Temperature = null;
        agentEnabledChatOptions.TopP = null;
        agentEnabledChatOptions.PresencePenalty = null;
        agentEnabledChatOptions.ResponseFormat = null;

        // Use the conversation from the request, or the one defined at the client level.
        agentEnabledChatOptions.ConversationId = options?.ConversationId ?? this._baseChatOptions?.ConversationId;

        // Preserve the original RawRepresentationFactory.
        var originalFactory = options?.RawRepresentationFactory;

        agentEnabledChatOptions.RawRepresentationFactory = (client) =>
        {
            if (originalFactory?.Invoke(this) is not CreateResponseOptions responseCreationOptions)
            {
                responseCreationOptions = new CreateResponseOptions();
            }

            responseCreationOptions.Agent = this._agentReference;
#pragma warning disable SCME0001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
            responseCreationOptions.Patch.Remove("$.model"u8);
#pragma warning restore SCME0001

            return responseCreationOptions;
        };

        return agentEnabledChatOptions;
    }

    private static AgentReference CreateAgentReference(ProjectsAgentVersion agentVersion)
    {
        // If the version is null, empty, or whitespace, use "latest" as the default. This handles
        // cases where hosted agents (like MCP agents) may not have a version assigned.
        var version = string.IsNullOrWhiteSpace(agentVersion.Version) ? "latest" : agentVersion.Version;
        return new AgentReference(agentVersion.Name, version);
    }

    private static HostedAgentEndpointInner BuildHostedAgentEndpointInner(
        Uri agentEndpoint,
        AuthenticationTokenProvider credential,
        ProjectOpenAIClientOptions? clientOptions)
    {
        Throw.IfNull(agentEndpoint);
        Throw.IfNull(credential);

        var (agentName, _) = ParseAgentEndpoint(agentEndpoint);

        var perAgentOptions = clientOptions ?? new ProjectOpenAIClientOptions();
        perAgentOptions.Endpoint = agentEndpoint;
        perAgentOptions.AgentName = agentName;

        var authPolicy = new BearerTokenPolicy(credential, AzureAiResourceScope);
        var perAgentClient = new ProjectOpenAIClient(authPolicy, perAgentOptions);

        var chatClient = perAgentClient.GetProjectResponsesClient().AsIChatClient();
        return new HostedAgentEndpointInner(chatClient, perAgentClient, agentName);
    }

    /// <summary>Best-effort registration of <see cref="AgentFrameworkUserAgentPolicy"/> via the MEAI <see cref="OpenAIRequestPolicies"/> hook with at-most-once dedup per pipeline.</summary>
    private static void TryRegisterAgentFrameworkUserAgentPolicy(IChatClient? innerClient)
    {
        if (innerClient?.GetService<OpenAIRequestPolicies>() is { } policies)
        {
            // OpenAIRequestPoliciesReflection.AddPolicyIfMissing performs a check-then-add against
            // the private _entries collection on the OpenAIRequestPolicies instance, so the
            // policy is registered at most once even when many FoundryChatClient instances share
            // the same underlying chat client.
            OpenAIRequestPoliciesReflection.AddPolicyIfMissing(
                policies,
                AgentFrameworkUserAgentPolicy.Instance,
                PipelinePosition.PerCall);
        }
    }

    /// <summary>Default OAuth scope for the Azure AI resource. Matches the scope used by <c>Azure.AI.Extensions.OpenAI</c>'s internal authentication helper so the bearer token is accepted by the Foundry control plane.</summary>
    private const string AzureAiResourceScope = "https://ai.azure.com/.default";

    private readonly struct HostedAgentEndpointInner
    {
        public HostedAgentEndpointInner(IChatClient chatClient, ProjectOpenAIClient perAgentClient, string agentName)
        {
            this.ChatClient = chatClient;
            this.PerAgentClient = perAgentClient;
            this.AgentName = agentName;
        }

        public IChatClient ChatClient { get; }
        public ProjectOpenAIClient PerAgentClient { get; }
        public string AgentName { get; }
    }
}
