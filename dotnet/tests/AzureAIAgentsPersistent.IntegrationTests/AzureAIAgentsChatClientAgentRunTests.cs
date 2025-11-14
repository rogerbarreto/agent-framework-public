// Copyright (c) Microsoft. All rights reserved.

using AgentConformance.IntegrationTests;

namespace AzureAI.IntegrationTests;

public class AzureAIAgentsChatClientAgentRunTests() : ChatClientAgentRunTests<AzureAIAgentsPersistentFixture>(() => new())
{
}
