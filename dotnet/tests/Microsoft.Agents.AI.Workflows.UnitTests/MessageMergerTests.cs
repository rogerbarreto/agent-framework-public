// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Workflows.UnitTests;

public class MessageMergerTests
{
    public static string TestAgentId1 => "TestAgent1";
    public static string TestAgentId2 => "TestAgent2";

    public static string TestAuthorName1 => "Assistant1";
    public static string TestAuthorName2 => "Assistant2";

    [Fact]
    public void Test_MessageMerger_AssemblesMessage()
    {
        DateTimeOffset creationTime = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromSeconds(1));
        string responseId = Guid.NewGuid().ToString("N");
        string messageId = Guid.NewGuid().ToString("N");

        MessageMerger merger = new();

        foreach (AgentResponseUpdate update in "Hello Agent Framework Workflows!".ToAgentRunStream(authorName: TestAuthorName1, agentId: TestAgentId1, messageId: messageId, createdAt: creationTime, responseId: responseId))
        {
            merger.AddUpdate(update);
        }

        AgentResponse response = merger.ComputeMerged(responseId);

        response.Messages.Should().HaveCount(1);
        response.Messages[0].Role.Should().Be(ChatRole.Assistant);
        response.Messages[0].AuthorName.Should().Be(TestAuthorName1);
        response.AgentId.Should().Be(TestAgentId1);
        response.CreatedAt.Should().HaveValue();
        response.CreatedAt.Value.Should().BeOnOrAfter(creationTime);
        response.CreatedAt.Value.Should().BeCloseTo(creationTime, precision: TimeSpan.FromSeconds(5));
        response.Messages[0].CreatedAt.Should().Be(creationTime);
        response.Messages[0].Contents.Should().HaveCount(1);
        response.FinishReason.Should().BeNull();
    }

    [Fact]
    public void Test_MessageMerger_PropagatesFinishReasonFromUpdates()
    {
        // Arrange
        string responseId = Guid.NewGuid().ToString("N");
        string messageId = Guid.NewGuid().ToString("N");

        MessageMerger merger = new();

        foreach (AgentResponseUpdate update in "Hello".ToAgentRunStream(agentId: TestAgentId1, messageId: messageId, responseId: responseId))
        {
            merger.AddUpdate(update);
        }

        // Add a final update with FinishReason set
        merger.AddUpdate(new AgentResponseUpdate
        {
            ResponseId = responseId,
            MessageId = messageId,
            FinishReason = ChatFinishReason.ContentFilter,
            Role = ChatRole.Assistant,
        });

        // Act
        AgentResponse response = merger.ComputeMerged(responseId);

        // Assert - FinishReason from the update should propagate through
        response.FinishReason.Should().Be(ChatFinishReason.ContentFilter);
    }

    [Fact]
    public void Test_MessageMerger_NullMessageId_CoalescesWithAdjacentUpdates()
    {
        // Simulates the MEAI OpenAIResponsesChatClient behavior where reasoning
        // content streams with MessageId = null while text gets a proper MessageId.
        // The merger should preserve insertion order so that ToChatResponse()
        // coalesces null-MessageId updates with the surrounding message.
        DateTimeOffset creationTime = DateTimeOffset.UtcNow;
        string responseId = Guid.NewGuid().ToString("N");
        string textMessageId = Guid.NewGuid().ToString("N");

        MessageMerger merger = new();

        // Reasoning updates with null MessageId (the MEAI bug)
        merger.AddUpdate(new AgentResponseUpdate
        {
            Role = ChatRole.Assistant,
            AuthorName = TestAuthorName1,
            Contents = [new TextReasoningContent("Let me think...")],
            ResponseId = responseId,
            AgentId = TestAgentId1,
            CreatedAt = creationTime,
            MessageId = null,
        });

        merger.AddUpdate(new AgentResponseUpdate
        {
            Contents = [new TextReasoningContent(" 2 + 2 = 4.")],
            ResponseId = responseId,
            AgentId = TestAgentId1,
            CreatedAt = creationTime,
            MessageId = null,
        });

        // Text updates with a proper MessageId
        merger.AddUpdate(new AgentResponseUpdate
        {
            Role = ChatRole.Assistant,
            AuthorName = TestAuthorName1,
            Contents = [new TextContent("The answer is ")],
            ResponseId = responseId,
            AgentId = TestAgentId1,
            CreatedAt = creationTime,
            MessageId = textMessageId,
        });

        merger.AddUpdate(new AgentResponseUpdate
        {
            Contents = [new TextContent("4.")],
            ResponseId = responseId,
            AgentId = TestAgentId1,
            CreatedAt = creationTime,
            MessageId = textMessageId,
        });

        AgentResponse response = merger.ComputeMerged(responseId);

        // Reasoning and text should be coalesced into the same message
        // because null MessageId is treated as "same message" by ToChatResponse().
        response.Messages.Should().HaveCount(1);
        response.Messages[0].Role.Should().Be(ChatRole.Assistant);
        response.Messages[0].AuthorName.Should().Be(TestAuthorName1);

        var reasoningContents = response.Messages[0].Contents.OfType<TextReasoningContent>().ToList();
        var textContents = response.Messages[0].Contents.OfType<TextContent>().ToList();

        reasoningContents.Should().NotBeEmpty("reasoning content should be in the message");
        textContents.Should().NotBeEmpty("text content should be in the message");
    }
}
