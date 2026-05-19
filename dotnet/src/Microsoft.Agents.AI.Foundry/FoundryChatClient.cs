// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;
using OpenAI.Files;
using OpenAI.Responses;
using OpenAI.VectorStores;

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
public sealed class FoundryChatClient : DelegatingChatClient
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
        this._aiProjectClient = inner.AIProjectClient;
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

    #region File and vector-store helpers (mirrors Python's foundry_chat_client surface)

    /// <summary>
    /// Uploads a single file to the project for the supplied purpose. The upload is performed
    /// against the project-level <see cref="AIProjectClient"/> reachable via
    /// <see cref="GetService(Type, object?)"/>, so this method works uniformly across all three
    /// FoundryChatClient construction modes.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the file to upload. The file must exist.</param>
    /// <param name="purpose">The file upload purpose (e.g. <see cref="FileUploadPurpose.Assistants"/>).</param>
    /// <param name="cancellationToken">A token that can cancel the upload.</param>
    /// <returns>The created <see cref="OpenAIFile"/> as returned by the service.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">The file at <paramref name="filePath"/> does not exist.</exception>
    public async Task<OpenAIFile> UploadFileAsync(string filePath, FileUploadPurpose purpose, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: '{filePath}'.", filePath);
        }

        var fileClient = this.GetOpenAIFileClient();
        // Use the Stream overload to honor cancellation; the (string, purpose) overload has no
        // CancellationToken parameter in the OpenAI SDK.
        using var stream = File.OpenRead(filePath);
        var result = await fileClient.UploadFileAsync(stream, Path.GetFileName(filePath), purpose, cancellationToken).ConfigureAwait(false);
        return result.Value;
    }

    /// <summary>Deletes a file previously uploaded to the project.</summary>
    /// <param name="fileId">The file id returned by <see cref="UploadFileAsync(string, FileUploadPurpose, CancellationToken)"/>.</param>
    /// <param name="cancellationToken">A token that can cancel the delete.</param>
    /// <returns>The deletion result.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileId"/> is <see langword="null"/> or whitespace.</exception>
    public async Task<FileDeletionResult> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhitespace(fileId);
        var fileClient = this.GetOpenAIFileClient();
        var result = await fileClient.DeleteFileAsync(fileId, cancellationToken).ConfigureAwait(false);
        return result.Value;
    }

    /// <summary>
    /// Uploads the supplied files, creates a vector store containing them, waits until the
    /// store is fully ready, and returns the <see cref="VectorStore"/>. Mirrors Python's
    /// <c>foundry_chat_client.create_vector_store(name, files, expires_after_days)</c>.
    /// </summary>
    /// <param name="name">The vector store name.</param>
    /// <param name="filePaths">Paths to files to upload and attach to the store.</param>
    /// <param name="expiresAfter">Optional last-active-at expiration window. When supplied, the vector store expires this many days after its last use.</param>
    /// <param name="cancellationToken">A token that can cancel the orchestration.</param>
    /// <returns>The created and fully-ready <see cref="VectorStore"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or whitespace, or <paramref name="filePaths"/> is <see langword="null"/>.</exception>
    public async Task<VectorStore> CreateVectorStoreAsync(string name, IEnumerable<string> filePaths, TimeSpan? expiresAfter = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhitespace(name);
        Throw.IfNull(filePaths);

        var fileIds = new List<string>();
        foreach (var path in filePaths)
        {
            var uploaded = await this.UploadFileAsync(path, FileUploadPurpose.Assistants, cancellationToken).ConfigureAwait(false);
            fileIds.Add(uploaded.Id);
        }

        var options = new VectorStoreCreationOptions
        {
            Name = name,
        };
        foreach (var id in fileIds)
        {
            options.FileIds.Add(id);
        }
        if (expiresAfter is { } window)
        {
            options.ExpirationPolicy = new VectorStoreExpirationPolicy(VectorStoreExpirationAnchor.LastActiveAt, (int)Math.Ceiling(window.TotalDays));
        }

        var vectorStoreClient = this.GetVectorStoreClient();
        var result = await vectorStoreClient.CreateVectorStoreAsync(options, cancellationToken).ConfigureAwait(false);
        return result.Value;
    }

    /// <summary>Deletes a vector store. The associated files (if any) are not deleted by this method; call <see cref="DeleteFileAsync(string, CancellationToken)"/> separately to clean them up.</summary>
    /// <param name="vectorStoreId">The vector store id.</param>
    /// <param name="cancellationToken">A token that can cancel the delete.</param>
    /// <returns>The deletion result.</returns>
    /// <exception cref="ArgumentException"><paramref name="vectorStoreId"/> is <see langword="null"/> or whitespace.</exception>
    public async Task<VectorStoreDeletionResult> DeleteVectorStoreAsync(string vectorStoreId, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhitespace(vectorStoreId);
        var vectorStoreClient = this.GetVectorStoreClient();
        var result = await vectorStoreClient.DeleteVectorStoreAsync(vectorStoreId, cancellationToken).ConfigureAwait(false);
        return result.Value;
    }

    private OpenAIFileClient GetOpenAIFileClient()
    {
        var projectClient = this._aiProjectClient
            ?? throw new InvalidOperationException("This FoundryChatClient does not have an AIProjectClient available. File and vector-store helpers require an AIProjectClient.");
        return projectClient.GetProjectOpenAIClient().GetOpenAIFileClient();
    }

    private VectorStoreClient GetVectorStoreClient()
    {
        var projectClient = this._aiProjectClient
            ?? throw new InvalidOperationException("This FoundryChatClient does not have an AIProjectClient available. File and vector-store helpers require an AIProjectClient.");
        return projectClient.GetProjectOpenAIClient().GetVectorStoreClient();
    }

    #endregion

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

        var (agentName, projectRoot) = ParseAgentEndpoint(agentEndpoint);

        var perAgentOptions = clientOptions ?? new ProjectOpenAIClientOptions();
        perAgentOptions.Endpoint = agentEndpoint;
        perAgentOptions.AgentName = agentName;

        var authPolicy = new BearerTokenPolicy(credential, AzureAiResourceScope);
        var perAgentClient = new ProjectOpenAIClient(authPolicy, perAgentOptions);

        var chatClient = perAgentClient.GetProjectResponsesClient().AsIChatClient();

        // Materialize a project-level AIProjectClient from the parsed project root so
        // GetService<AIProjectClient>() returns non-null for all FoundryChatClient
        // construction modes. Project-level helpers (file upload, vector store create/delete)
        // depend on this. RBAC for those calls is at the project level; if the supplied
        // credential lacks project-scope permissions, the SDK surfaces a clean 401/403 at
        // call time. The four observable primitive ClientPipelineOptions properties are
        // propagated from the caller's per-agent options bag so test-injected transports and
        // explicit RetryPolicy / NetworkTimeout / UserAgentApplicationId reach the
        // project-level pipeline. Pipeline policies added via AddPolicy on the caller bag are
        // NOT propagated because ClientPipelineOptions does not publicly enumerate policies.
        var aiProjectClientOptions = new AIProjectClientOptions();
        if (clientOptions is not null)
        {
            if (clientOptions.RetryPolicy is not null)
            {
                aiProjectClientOptions.RetryPolicy = clientOptions.RetryPolicy;
            }
            if (clientOptions.NetworkTimeout is not null)
            {
                aiProjectClientOptions.NetworkTimeout = clientOptions.NetworkTimeout;
            }
            if (clientOptions.Transport is not null)
            {
                aiProjectClientOptions.Transport = clientOptions.Transport;
            }
            if (!string.IsNullOrEmpty(clientOptions.UserAgentApplicationId))
            {
                aiProjectClientOptions.UserAgentApplicationId = clientOptions.UserAgentApplicationId;
            }
        }
        var aiProjectClient = new AIProjectClient(projectRoot, credential, aiProjectClientOptions);

        return new HostedAgentEndpointInner(chatClient, perAgentClient, aiProjectClient, agentName);
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
        public HostedAgentEndpointInner(IChatClient chatClient, ProjectOpenAIClient perAgentClient, AIProjectClient aiProjectClient, string agentName)
        {
            this.ChatClient = chatClient;
            this.PerAgentClient = perAgentClient;
            this.AIProjectClient = aiProjectClient;
            this.AgentName = agentName;
        }

        public IChatClient ChatClient { get; }
        public ProjectOpenAIClient PerAgentClient { get; }
        public AIProjectClient AIProjectClient { get; }
        public string AgentName { get; }
    }
}
