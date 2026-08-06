// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Reports, on the <c>GET /readiness</c> probe, a registered agent configured to have its own service
/// store the responses it produces, so a container that would record the conversation twice is caught
/// before it takes any traffic.
/// </summary>
/// <remarks>
/// <para>
/// Each agent is run for real, with its chat client replaced for that run by
/// <see cref="StoredOutputProbeChatClient"/>, which answers without calling anything. The run therefore
/// builds the very request the agent would have sent, and the probe reads the store setting off it.
/// Nothing hosting adds per request is applied here, so what the probe sees is how the container
/// configured its agent. Nothing leaves the container either.
/// </para>
/// <para>
/// Only a confirmed "this asks to be stored" fails the probe. An agent that is not a
/// <see cref="ChatClientAgent"/>, a request that carries no such setting, and a run that could not be
/// completed are all reported as healthy: this package cannot tell what those would do, and a
/// readiness probe is the wrong place to turn an uncertainty into an outage.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
internal sealed class HostedStoredOutputHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FoundryResponsesOptions _hostingOptions;
    private readonly ILogger<HostedStoredOutputHealthCheck>? _logger;

    public HostedStoredOutputHealthCheck(
        IServiceProvider serviceProvider,
        IOptions<FoundryResponsesOptions>? hostingOptions = null,
        ILogger<HostedStoredOutputHealthCheck>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        this._serviceProvider = serviceProvider;
        this._hostingOptions = hostingOptions?.Value ?? new FoundryResponsesOptions();
        this._logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (this._hostingOptions.AllowStoredOutputEnabled)
        {
            return HealthCheckResult.Healthy(
                "Stored output: the container allows the agent's own service to store responses, so its configuration is left alone.");
        }

        List<string> storingAgents = [];
        var checkedAgents = 0;

        foreach (var agent in this.ResolveAgents())
        {
            if (agent.GetService<ChatClientAgent>() is null)
            {
                // Hosting only reaches the store setting through ChatClientAgent's chat options, so any
                // other agent runs untouched and there is nothing to report.
                continue;
            }

            checkedAgents++;
            if (await this.StoresItsOwnResponsesAsync(agent, cancellationToken).ConfigureAwait(false))
            {
                storingAgents.Add(agent.Name ?? agent.Id);
            }
        }

        if (storingAgents.Count > 0)
        {
            return new HealthCheckResult(
                status: context.Registration.FailureStatus,
                description: string.Create(
                    CultureInfo.InvariantCulture,
                    $"Stored output: {storingAgents.Count} registered agent(s) should not have server side storage enabled. This will produce a new untracked conversation/response in the server while the hosted agent will also generate a conversation for the request of the agent. This setting is only allowed when enabling the FoundryResponsesOptions.AllowStoredOutputEnabled flag, which leaves the agent's own storage setting untouched and keeps that second recording on purpose."),
                data: new Dictionary<string, object>(StringComparer.Ordinal) { ["storingAgents"] = storingAgents });
        }

        return HealthCheckResult.Healthy(
            string.Create(CultureInfo.InvariantCulture, $"Stored output: {checkedAgents} agent(s) checked, none asking to store responses of their own."));
    }

    /// <summary>
    /// Runs the agent with its chat client replaced by one that calls nothing, and reports whether the
    /// request the agent built asks for the response to be stored.
    /// </summary>
    /// <remarks>
    /// The run carries no chat options of its own, so the agent's own configuration is what reaches the
    /// probe. Overriding the setting here, the way the request handler does per turn, would only show
    /// the override back.
    /// </remarks>
    private async Task<bool> StoresItsOwnResponsesAsync(AIAgent agent, CancellationToken cancellationToken)
    {
        var probe = new StoredOutputProbeChatClient();
        var runOptions = new ChatClientAgentRunOptions { ChatClientFactory = _ => probe };

        try
        {
            await agent.RunAsync([], options: runOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The agent could not complete a run it was never really asked to answer. That says nothing
            // about how it stores responses, so it is not held against it.
            if (this._logger?.IsEnabled(LogLevel.Debug) is true)
            {
                this._logger.LogDebug(ex, "Could not probe the stored output setting for agent '{AgentName}'.", agent.Name);
            }

            return false;
        }

        if (probe.StoredOutputEnabled is null && this._logger?.IsEnabled(LogLevel.Debug) is true)
        {
            this._logger.LogDebug(
                "Agent '{AgentName}' builds a request that carries no stored output setting, so whether its service would store responses could not be determined.",
                agent.Name);
        }

        return probe.StoredOutputEnabled is true;
    }

    /// <summary>
    /// Every agent this container can serve: the ones registered under a name, plus the default.
    /// </summary>
    private List<AIAgent> ResolveAgents()
    {
        var agents = new List<AIAgent>(this._serviceProvider.GetKeyedServices<AIAgent>(KeyedService.AnyKey));

        if (this._serviceProvider.GetService<AIAgent>() is { } defaultAgent && !agents.Contains(defaultAgent))
        {
            agents.Add(defaultAgent);
        }

        return agents;
    }
}
