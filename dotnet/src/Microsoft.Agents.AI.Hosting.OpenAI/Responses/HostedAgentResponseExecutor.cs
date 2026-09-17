// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses;

/// <summary>
/// Response executor that routes requests to hosted AIAgent services based on agent.name or metadata["entity_id"].
/// This executor resolves agents from keyed services registered via AddAIAgent().
/// The model field is reserved for actual model names and is never used for entity/agent identification.
/// </summary>
internal sealed class HostedAgentResponseExecutor : IResponseExecutor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HostedAgentResponseExecutor> _logger;
    private readonly OpenAIResponsesMapOptions _mapOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedAgentResponseExecutor"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve hosted agents.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="mapOptions">Options controlling how incoming requests are mapped onto the agent run.</param>
    public HostedAgentResponseExecutor(
        IServiceProvider serviceProvider,
        ILogger<HostedAgentResponseExecutor> logger,
        OpenAIResponsesMapOptions? mapOptions = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this._serviceProvider = serviceProvider;
        this._logger = logger;
        this._mapOptions = mapOptions ?? new OpenAIResponsesMapOptions();
    }

    /// <inheritdoc/>
    public ValueTask<ResponseError?> ValidateRequestAsync(
        CreateResponse request,
        CancellationToken cancellationToken = default)
    {
        // Extract agent name from agent.name or model parameter
        string? agentName = GetAgentName(request);

        if (string.IsNullOrEmpty(agentName))
        {
            return ValueTask.FromResult<ResponseError?>(new ResponseError
            {
                Code = "missing_required_parameter",
                Message = "No 'agent.name' or 'metadata[\"entity_id\"]' specified in the request."
            });
        }

        // Validate that the agent can be resolved
        AIAgent? agent = this._serviceProvider.GetKeyedService<AIAgent>(agentName);
        if (agent is null)
        {
            if (this._logger.IsEnabled(LogLevel.Warning))
            {
                this._logger.LogWarning("Failed to resolve agent with name '{AgentName}'", agentName);
            }

            return ValueTask.FromResult<ResponseError?>(new ResponseError
            {
                Code = "agent_not_found",
                Message = $"""
                    Agent '{agentName}' not found.
                    Ensure the agent is registered with '{agentName}' name in the dependency injection container.
                    We recommend using 'builder.AddAIAgent()' for simplicity.
                """
            });
        }

        // An approval response can be validated only against the pending request stored by the server.
#pragma warning disable MAAI001
        bool hasSessionStore = this._serviceProvider.GetKeyedService<AgentSessionStore>(agentName) is not null;
#pragma warning restore MAAI001
        ResponseError? sessionError = AgentResponseExecution.ValidateSessionRequirements(request, hasSessionStore);
        if (sessionError is not null)
        {
            return ValueTask.FromResult<ResponseError?>(sessionError);
        }

        // Surface unsupported request settings as a clean request error rather than an unhandled
        // exception during execution.
        try
        {
            _ = this._mapOptions.RunOptionsFactory(request.ToRequestInfo());
        }
        catch (NotSupportedException ex)
        {
            return ValueTask.FromResult<ResponseError?>(new ResponseError
            {
                Code = "unsupported_parameter",
                Message = ex.Message
            });
        }

        return ValueTask.FromResult<ResponseError?>(null);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamingResponseEvent> ExecuteAsync(
        AgentInvocationContext context,
        CreateResponse request,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Resolve the agent selected by this request together with its optional persisted session store.
        string agentName = GetAgentName(request)!;
        AIAgent agent = this._serviceProvider.GetRequiredKeyedService<AIAgent>(agentName);
#pragma warning disable MAAI001
        AgentSessionStore? sessionStore = this._serviceProvider.GetKeyedService<AgentSessionStore>(agentName);
#pragma warning restore MAAI001
        AIAgent executionAgent = sessionStore is null ? agent : new AIHostAgent(agent, sessionStore);

        // Once selected, hosted agents use the same execution behavior as fixed-agent endpoints.
        await foreach (StreamingResponseEvent streamingEvent in AgentResponseExecution.ExecuteAsync(
            executionAgent, this._mapOptions, context, request, conversationHistory, cancellationToken).ConfigureAwait(false))
        {
            yield return streamingEvent;
        }
    }

    /// <summary>
    /// Extracts the agent name for a request from the agent.name property, falling back to metadata["entity_id"].
    /// </summary>
    /// <param name="request">The create response request.</param>
    /// <returns>The agent name.</returns>
    private static string? GetAgentName(CreateResponse request)
    {
        string? agentName = request.Agent?.Name;

        // Fall back to metadata["entity_id"] if agent.name is not present
        if (string.IsNullOrEmpty(agentName) && request.Metadata?.TryGetValue("entity_id", out string? entityId) == true)
        {
            agentName = entityId;
        }

        return agentName;
    }
}
