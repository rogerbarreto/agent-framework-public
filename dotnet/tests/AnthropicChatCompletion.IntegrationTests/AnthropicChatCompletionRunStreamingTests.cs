// Copyright (c) Microsoft. All rights reserved.

using AgentConformance.IntegrationTests;

namespace AnthropicChatCompletion.IntegrationTests;

public class AnthropicChatCompletionRunStreamingTests()
    : RunStreamingTests<AnthropicChatCompletionFixture>(() => new(useReasoningChatModel: false))
{
}

public class AnthropicChatCompletionReasoningRunStreamingTests()
    : RunStreamingTests<AnthropicChatCompletionFixture>(() => new(useReasoningChatModel: true))
{
}
