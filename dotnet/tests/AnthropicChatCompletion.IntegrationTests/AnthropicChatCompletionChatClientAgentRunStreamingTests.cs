// Copyright (c) Microsoft. All rights reserved.

using AgentConformance.IntegrationTests;

namespace AnthropicChatCompletion.IntegrationTests;

public class AnthropicChatCompletionChatClientAgentRunStreamingTests()
    : ChatClientAgentRunStreamingTests<AnthropicChatCompletionFixture>(() => new(useReasoningChatModel: false))
{
}

public class AnthropicChatCompletionChatClientAgentReasoningRunStreamingTests()
    : ChatClientAgentRunStreamingTests<AnthropicChatCompletionFixture>(() => new(useReasoningChatModel: true))
{
}
