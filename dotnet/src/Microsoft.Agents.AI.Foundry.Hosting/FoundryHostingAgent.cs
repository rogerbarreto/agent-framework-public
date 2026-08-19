// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Carries Foundry hosting metadata alongside the agent served by the response handler.
/// </summary>
/// <remarks>
/// This wrapper is the central extension point for hosting-specific agent metadata. Outer agent
/// middleware continues to expose it through <see cref="AIAgent.GetService{TService}(object?)"/>.
/// </remarks>
internal sealed class FoundryHostingAgent : DelegatingAIAgent
{
    internal FoundryHostingAgent(AIAgent innerAgent, string sessionStorageIdentity)
        : base(innerAgent)
    {
        this.SessionStorageIdentity = Throw.IfNullOrWhitespace(sessionStorageIdentity);
    }

    internal string SessionStorageIdentity { get; }

    internal static string ResolveSessionStorageIdentity(
        AIAgent agent,
        string? registrationKey,
        AIAgent? defaultAgent)
    {
        _ = Throw.IfNull(agent);

        if (registrationKey is not null && !ReferenceEquals(agent, defaultAgent))
        {
            return $"key:{registrationKey}";
        }

        return !string.IsNullOrWhiteSpace(agent.Name)
            ? $"name:{agent.Name}"
            : "default";
    }
}
