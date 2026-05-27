// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics;

namespace Microsoft.Agents.AI;

/// <summary>Provides constants used by various telemetry services.</summary>
internal static class OpenTelemetryConsts
{
    public const string DefaultSourceName = "Experimental.Microsoft.Agents.AI";

    /// <summary>
    /// Shared <see cref="ActivitySource"/> instance used by <see cref="ChatClientAgent"/>'s
    /// self-telemetry-wrap suppression check. Only used for <see cref="ActivitySource.HasListeners"/>;
    /// the actual span emission is performed by the <see cref="OpenTelemetryAgent"/> instance
    /// that <see cref="ChatClientAgent"/> wraps itself with.
    /// </summary>
    public static readonly ActivitySource AgentActivitySource = new(DefaultSourceName);

    /// <summary>
    /// Key for an <see cref="Activity"/> custom property that marks the
    /// activity as the owner of an <c>invoke_agent</c> scope for a specific
    /// <see cref="ChatClientAgent"/> instance. The value stored is the
    /// <see cref="ChatClientAgent"/> reference itself; suppression checks compare via
    /// <see cref="object.ReferenceEquals(object?, object?)"/>.
    /// </summary>
    /// <remarks>
    /// Custom properties are not exported as OTLP span attributes — they are process-local
    /// state used purely to coordinate suppression between
    /// <see cref="OpenTelemetryAgent.UpdateCurrentActivity(Activity?)"/>
    /// and <see cref="ChatClientAgent"/>'s self-wrap logic.
    /// </remarks>
    public const string OwnedInvokeAgentScopeMarker = "Microsoft.Agents.AI.OpenTelemetry.OwnedInvokeAgentScope";

    public static class GenAI
    {
        public const string InvokeAgent = "invoke_agent";

        public static class Agent
        {
            public const string Id = "gen_ai.agent.id";
            public const string Name = "gen_ai.agent.name";
            public const string Description = "gen_ai.agent.description";
        }

        public static class Operation
        {
            public const string Name = "gen_ai.operation.name";
        }

        public static class Provider
        {
            public const string Name = "gen_ai.provider.name";
        }
    }
}
