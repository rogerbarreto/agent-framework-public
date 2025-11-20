// Copyright (c) Microsoft. All rights reserved.

using AgentConformance.IntegrationTests;

namespace AnthropicChatCompletion.IntegrationTests;

public class AnthropicChatCompletionChatClientAgentRunTests()
    : ChatClientAgentRunTests<AnthropicChatCompletionFixture>(() => new(useReasoningChatModel: false))
{
}

public class AnthropicChatCompletionChatClientAgentReasoningRunTests()
    : ChatClientAgentRunTests<AnthropicChatCompletionFixture>(() => new(useReasoningChatModel: true))
{
}
