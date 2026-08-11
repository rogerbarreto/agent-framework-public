// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Reports, on the <c>GET /readiness</c> probe, a registered workflow agent that was built with a
/// checkpoint manager of its own, so a container whose workflow state would be written somewhere
/// hosting cannot manage is caught before it takes any traffic.
/// </summary>
/// <remarks>
/// <para>
/// A hosted workflow has its checkpoints redirected to the Foundry durable state store, so that the
/// state a conversation builds up survives the container being restarted or replaced and is readable
/// by every instance of the agent. An agent that already names its own checkpoint manager is left
/// alone, because overriding an explicit choice silently would be worse. The result is a container
/// whose workflow state goes somewhere hosting does not manage, which is reported here rather than
/// discovered later.
/// </para>
/// <para>
/// Only an agent that runs a workflow is considered. Everything else, a <see cref="ChatClientAgent"/>
/// or an agent written by the container author, has no checkpoints and is passed over.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
internal sealed class HostedWorkflowCheckpointingHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public HostedWorkflowCheckpointingHealthCheck(IServiceProvider serviceProvider)
    {
        _ = Throw.IfNull(serviceProvider);

        this._serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Whether the process is running inside a Foundry container. Settable so a test does not depend
    /// on the process-wide, statically-cached <see cref="FoundryEnvironment.IsHosted"/> value.
    /// </summary>
    internal bool IsHosted { get; set; } = FoundryEnvironment.IsHosted;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(context);

        if (!this.IsHosted)
        {
            // Nothing redirects checkpoints outside a Foundry container, so an agent that brings its
            // own checkpoint manager is not competing with anything.
            return Task.FromResult(HealthCheckResult.Healthy(
                "Workflow checkpointing: not running in a Foundry container, so workflow checkpoints are left where each agent puts them."));
        }

        List<string> agentsWithOwnCheckpointing = [];
        var checkedAgents = 0;

        foreach (var agent in FoundryHostingExtensions.ResolveRegisteredAgents(this._serviceProvider))
        {
            if (agent.GetService<WorkflowAgentMetadata>() is not { } metadata)
            {
                continue;
            }

            checkedAgents++;
            if (metadata.UsesOwnCheckpointStorage)
            {
                agentsWithOwnCheckpointing.Add(agent.Name ?? agent.Id);
            }
        }

        if (agentsWithOwnCheckpointing.Count > 0)
        {
            return Task.FromResult(new HealthCheckResult(
                status: context.Registration.FailureStatus,
                description: string.Create(
                    CultureInfo.InvariantCulture,
                    $"Workflow checkpointing: {agentsWithOwnCheckpointing.Count} registered workflow agent(s) were built with a checkpoint manager of their own. A hosted workflow has its checkpoints written to the Foundry state store so they survive the container being replaced and can be read by every instance; an agent that names its own manager keeps that state somewhere this container does not manage. Build the agent without passing an execution environment configured with WithCheckpointing, and let hosting supply the store."),
                data: new Dictionary<string, object>(StringComparer.Ordinal) { ["agentsWithOwnCheckpointing"] = agentsWithOwnCheckpointing }));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            string.Create(CultureInfo.InvariantCulture, $"Workflow checkpointing: {checkedAgents} workflow agent(s) checked, all leaving their checkpoint storage to hosting.")));
    }
}
