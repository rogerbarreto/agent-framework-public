// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Foundry.Hosting.TestUtilities;

/// <summary>
/// xUnit collection that serializes tests mutating the <c>AGENTSERVER_STATE_ROOT</c> and
/// <c>FOUNDRY_HOSTING_ENVIRONMENT</c> process environment variables that select the AgentServer
/// local state-store fallback. Each test assembly runs in its own process, so each one links this
/// definition to serialize its own tests.
/// </summary>
[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class FoundryStateStoreLocalFallbackCollectionDefinition
{
    public const string CollectionName = "Foundry state-store local fallback";
}
