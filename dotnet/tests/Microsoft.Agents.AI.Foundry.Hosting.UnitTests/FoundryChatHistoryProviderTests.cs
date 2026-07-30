// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.AgentServer.Responses;
using Azure.AI.AgentServer.Responses.Models;
using Microsoft.Extensions.AI;
using Moq;

namespace Microsoft.Agents.AI.Foundry.Hosting.UnitTests;

/// <summary>
/// Tests for <see cref="FoundryChatHistoryProvider"/>, the chat history provider that reads the
/// conversation from the Foundry platform instead of keeping a copy inside the container.
/// </summary>
public class FoundryChatHistoryProviderTests
{
    [Fact]
    public async Task InvokingAsync_ReturnsPlatformHistoryBeforeRequestMessagesAsync()
    {
        // Arrange
        var context = CreateContext([NewMessageItem("msg_1", "earlier turn")]);
        var provider = new FoundryChatHistoryProvider(context);
        var agent = CreateAgent();
        var session = new FakeSession();
        var input = new ChatMessage(ChatRole.User, "new input");

        // Act
        var result = await provider.InvokingAsync(
            new ChatHistoryProvider.InvokingContext(agent, session, [input]),
            CancellationToken.None);

        // Assert: the platform history comes first, then the caller's messages.
        var messages = result.ToList();
        Assert.Equal(2, messages.Count);
        Assert.Contains("earlier turn", messages[0].Text);
        Assert.Same(input, messages[1]);
    }

    [Fact]
    public async Task InvokingAsync_MarksPlatformHistoryAsChatHistoryAsync()
    {
        // Arrange
        var context = CreateContext([NewMessageItem("msg_1", "earlier turn")]);
        var provider = new FoundryChatHistoryProvider(context);

        // Act
        var result = await provider.InvokingAsync(
            new ChatHistoryProvider.InvokingContext(CreateAgent(), new FakeSession(), []),
            CancellationToken.None);

        // Assert: marking the platform messages as chat history is what keeps another provider from
        // storing them again as if this turn had produced them.
        var message = Assert.Single(result);
        Assert.Equal(AgentRequestMessageSourceType.ChatHistory, message.GetAgentRequestMessageSourceType());
    }

    [Fact]
    public async Task InvokingAsync_WithNoPlatformHistory_ReturnsOnlyRequestMessagesAsync()
    {
        // Arrange
        var context = CreateContext([]);
        var provider = new FoundryChatHistoryProvider(context);
        var input = new ChatMessage(ChatRole.User, "new input");

        // Act
        var result = await provider.InvokingAsync(
            new ChatHistoryProvider.InvokingContext(CreateAgent(), new FakeSession(), [input]),
            CancellationToken.None);

        // Assert
        Assert.Same(input, Assert.Single(result));
    }

    [Fact]
    public async Task InvokedAsync_StoresNothingInTheSessionAsync()
    {
        // Arrange
        var context = CreateContext([]);
        var provider = new FoundryChatHistoryProvider(context);
        var session = new FakeSession();

        // Act: a completed turn is reported to the provider.
        await provider.InvokedAsync(
            new ChatHistoryProvider.InvokedContext(
                CreateAgent(),
                session,
                [new ChatMessage(ChatRole.User, "input")],
                [new ChatMessage(ChatRole.Assistant, "answer")]),
            CancellationToken.None);

        // Assert: the platform already persists the response items, so nothing is copied into the
        // session state. A copy there would grow the persisted session and diverge from the platform.
        Assert.Empty(session.StateBag.Serialize().EnumerateObject());
    }

    private static ResponseContext CreateContext(IReadOnlyList<OutputItem> history)
    {
        var mock = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mock.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(history);
        return mock.Object;
    }

    private static AIAgent CreateAgent() => new Mock<AIAgent>().Object;

    private static OutputItemMessage NewMessageItem(string id, string text) =>
        new(
            id: id,
            role: MessageRole.Assistant,
            content: [new MessageContentOutputTextContent(text, Array.Empty<Annotation>(), Array.Empty<LogProb>())],
            status: MessageStatus.Completed);

    private sealed class FakeSession : AgentSession
    {
    }
}
