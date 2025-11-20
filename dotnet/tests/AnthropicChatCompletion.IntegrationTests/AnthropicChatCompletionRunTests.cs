// Copyright (c) Microsoft. All rights reserved.

using AgentConformance.IntegrationTests;

namespace AnthropicChatCompletion.IntegrationTests;

public class AnthropicChatCompletionRunTests()
    : RunTests<AnthropicChatCompletionFixture>(() => new(useReasoningChatModel: false))
{
}

public class AnthropicChatCompletionReasoningRunTests()
    : RunTests<AnthropicChatCompletionFixture>(() => new(useReasoningChatModel: true))
{
}
