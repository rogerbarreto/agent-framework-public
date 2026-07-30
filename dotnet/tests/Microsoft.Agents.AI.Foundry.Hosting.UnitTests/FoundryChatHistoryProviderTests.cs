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
        var provider = new FoundryChatHistoryProvider(context, serviceStoresThisTurn: true);
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
        var provider = new FoundryChatHistoryProvider(context, serviceStoresThisTurn: true);

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
        var provider = new FoundryChatHistoryProvider(context, serviceStoresThisTurn: true);
        var input = new ChatMessage(ChatRole.User, "new input");

        // Act
        var result = await provider.InvokingAsync(
            new ChatHistoryProvider.InvokingContext(CreateAgent(), new FakeSession(), [input]),
            CancellationToken.None);

        // Assert
        Assert.Same(input, Assert.Single(result));
    }

    [Fact]
    public async Task InvokedAsync_WhenTheServiceStoresTheTurn_KeepsNothingInTheSessionAsync()
    {
        // Arrange
        var context = CreateContext([]);
        var provider = new FoundryChatHistoryProvider(context, serviceStoresThisTurn: true);
        var session = new FakeSession();

        // Act: a completed turn is reported to the provider.
        await provider.InvokedAsync(
            new ChatHistoryProvider.InvokedContext(
                CreateAgent(),
                session,
                [new ChatMessage(ChatRole.User, "input")],
                [new ChatMessage(ChatRole.Assistant, "answer")]),
            CancellationToken.None);

        // Assert: the service already persists the items of a stored response, so nothing is copied into
        // the session. A copy there would grow the persisted session and drift from what the service serves.
        Assert.Empty(session.StateBag.Serialize().EnumerateObject());
    }

    [Fact]
    public async Task InvokedAsync_WhenTheServiceDoesNotStoreTheTurn_KeepsItInTheSessionAsync()
    {
        // Arrange: a turn the service was not asked to store.
        var context = CreateContext([]);
        var provider = new FoundryChatHistoryProvider(context, serviceStoresThisTurn: false);
        var session = new FakeSession();

        // Act
        await provider.InvokedAsync(
            new ChatHistoryProvider.InvokedContext(
                CreateAgent(),
                session,
                [new ChatMessage(ChatRole.User, "unstored input")],
                [new ChatMessage(ChatRole.Assistant, "unstored answer")]),
            CancellationToken.None);

        // Assert: the session is the only memory this turn can have, so it is kept there.
        var state = session.StateBag.Serialize().GetRawText();
        Assert.Contains("unstored input", state, StringComparison.Ordinal);
        Assert.Contains("unstored answer", state, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokingAsync_ReturnsServedHistoryThenTurnsTheServiceDidNotStoreAsync()
    {
        // Arrange: a conversation whose earlier turn the service holds, plus a later turn it was not
        // asked to store, which a previous request kept in the session.
        var context = CreateContext([NewMessageItem("msg_1", "stored turn")]);
        var session = new FakeSession();
        await new FoundryChatHistoryProvider(context, serviceStoresThisTurn: false).InvokedAsync(
            new ChatHistoryProvider.InvokedContext(
                CreateAgent(), session, [new ChatMessage(ChatRole.User, "unstored turn")], []),
            CancellationToken.None);

        // Act: a later request reads the conversation back.
        var result = await new FoundryChatHistoryProvider(context, serviceStoresThisTurn: false).InvokingAsync(
            new ChatHistoryProvider.InvokingContext(CreateAgent(), session, [new ChatMessage(ChatRole.User, "new input")]),
            CancellationToken.None);

        // Assert: the conversation is whole and in order, with each turn appearing once.
        Assert.Equal(
            ["stored turn", "unstored turn", "new input"],
            result.Select(m => m.Text).ToArray());
    }

    [Fact]
    public async Task ConversationAcrossInstancesAndStoreModesAsync()
    {
        // Arrange: one service-side conversation and three separate provider instances, each with its
        // own session. An instance only builds a local buffer once it is used for an unstored turn, and
        // an instance that has never done so starts from whatever the service last saved.
        var service = new FakeStoredResponses();
        var instance1 = new Instance();

        // 1. Stored turn on instance 1: nothing precedes it, and the service records it.
        Assert.Equal(["call 1"], await CallAsync(service, instance1, store: true, "call 1"));

        // The other two instances join the same conversation, so they start from the turn the service
        // last saved for it. Neither holds anything of its own yet.
        var instance2 = new Instance { LastStoredResponseId = instance1.LastStoredResponseId };
        var instance3 = new Instance { LastStoredResponseId = instance1.LastStoredResponseId };

        // 2. Unstored turn on instance 1: it reads the stored turn back and keeps its own turn locally.
        Assert.Equal(["call 1", "reply 1", "call 2"], await CallAsync(service, instance1, store: false, "call 2"));

        // 3. A different instance starts from the last saved turn only: instance 1's unstored turn is
        //    held in instance 1's own session and is invisible here.
        Assert.Equal(["call 1", "reply 1", "call 3"], await CallAsync(service, instance2, store: false, "call 3"));

        // 4. Asking the service to store a turn on an instance that is already holding unstored ones is
        //    refused: the service would record this turn without the turns that came before it.
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CallAsync(service, instance2, store: true, "call 4"));
        Assert.Contains("were not stored", refused.Message, StringComparison.Ordinal);

        // 5. Continuing unstored on the same instance is fine, and it keeps growing its local turns.
        Assert.Equal(
            ["call 1", "reply 1", "call 3", "reply 3", "call 5"],
            await CallAsync(service, instance2, store: false, "call 5"));

        // 6. Still refused, for the same reason.
        await Assert.ThrowsAsync<InvalidOperationException>(() => CallAsync(service, instance2, store: true, "call 6"));

        // 7. A fresh instance holds nothing locally, so it may store. It branches off the last turn the
        //    service saved, which is still the first one.
        Assert.Equal(["call 1", "reply 1", "call 7"], await CallAsync(service, instance3, store: true, "call 7"));

        // 8. Instance 2 is unaffected by instance 3's stored turn: that turn sits on another branch of
        //    the conversation, so it is not among the turns leading to instance 2's last saved one.
        Assert.Equal(
            ["call 1", "reply 1", "call 3", "reply 3", "call 5", "reply 5", "call 8"],
            await CallAsync(service, instance2, store: false, "call 8"));

        // 9. And instance 1 still sees its own thread of the conversation.
        Assert.Equal(
            ["call 1", "reply 1", "call 2", "reply 2", "call 9"],
            await CallAsync(service, instance1, store: false, "call 9"));
    }

    /// <summary>
    /// Runs one turn through a new provider, the way the host does: a provider is built for the request,
    /// asked for the messages to send, and then told what the turn produced. Returns the message texts
    /// the model would have received.
    /// </summary>
    private static async Task<string[]> CallAsync(FakeStoredResponses service, Instance instance, bool store, string text)
    {
        var context = CreateContext(service.TurnsLeadingTo(instance.LastStoredResponseId));
        var provider = new FoundryChatHistoryProvider(context, serviceStoresThisTurn: store);
        var agent = CreateAgent();

        var sent = (await provider.InvokingAsync(
            new ChatHistoryProvider.InvokingContext(agent, instance.Session, [new ChatMessage(ChatRole.User, text)]),
            CancellationToken.None)).ToList();

        var reply = new ChatMessage(ChatRole.Assistant, text.Replace("call", "reply", StringComparison.Ordinal));
        await provider.InvokedAsync(
            new ChatHistoryProvider.InvokedContext(agent, instance.Session, sent, [reply]),
            CancellationToken.None);

        if (store)
        {
            instance.LastStoredResponseId = service.Store(instance.LastStoredResponseId, [new ChatMessage(ChatRole.User, text), reply]);
        }

        return [.. sent.Select(m => m.Text)];
    }

    /// <summary>A provider instance's own session, plus the last turn the service saved for it.</summary>
    private sealed class Instance
    {
        public FakeSession Session { get; } = new();

        public string? LastStoredResponseId { get; set; }
    }

    /// <summary>
    /// Stands in for the responses the service keeps. Each stored response points at the one it followed,
    /// so asking for the turns leading to a response walks back through them, and a response stored on
    /// another branch is not among them.
    /// </summary>
    private sealed class FakeStoredResponses
    {
        private readonly Dictionary<string, (string? Previous, List<ChatMessage> Messages)> _stored = new(StringComparer.Ordinal);

        public string Store(string? previousResponseId, IReadOnlyList<ChatMessage> messages)
        {
            var id = $"resp_{this._stored.Count + 1}";
            this._stored[id] = (previousResponseId, [.. messages]);
            return id;
        }

        public IReadOnlyList<OutputItem> TurnsLeadingTo(string? responseId)
        {
            var chain = new List<ChatMessage>();
            for (var id = responseId; id is not null && this._stored.TryGetValue(id, out var entry); id = entry.Previous)
            {
                chain.InsertRange(0, entry.Messages);
            }

            return [.. chain.Select((m, i) => NewMessageItem($"msg_{i}", m.Text))];
        }
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
