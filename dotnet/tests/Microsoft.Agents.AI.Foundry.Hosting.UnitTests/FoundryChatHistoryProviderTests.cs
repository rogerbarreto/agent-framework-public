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
    public async Task InvokingAsync_WhatIsKeptBelongsToTheSessionNotToTheProviderObjectAsync()
    {
        // Arrange: one unstored turn kept through one provider object.
        var context = CreateContext([]);
        var session = new FakeSession();
        await new FoundryChatHistoryProvider(context, serviceStoresThisTurn: false).InvokedAsync(
            new ChatHistoryProvider.InvokedContext(
                CreateAgent(), session, [new ChatMessage(ChatRole.User, "kept turn")], []),
            CancellationToken.None);

        // Act: a brand new provider object reads that session, and another one reads a different session.
        var sameSession = await new FoundryChatHistoryProvider(context, serviceStoresThisTurn: false).InvokingAsync(
            new ChatHistoryProvider.InvokingContext(CreateAgent(), session, []),
            CancellationToken.None);
        var otherSession = await new FoundryChatHistoryProvider(context, serviceStoresThisTurn: false).InvokingAsync(
            new ChatHistoryProvider.InvokingContext(CreateAgent(), new FakeSession(), []),
            CancellationToken.None);

        // Assert: the turn follows the session it was kept in, not the object that kept it. A host builds
        // a new provider for every request, so anything held on the object itself would be lost at once.
        Assert.Equal(["kept turn"], sameSession.Select(m => m.Text).ToArray());
        Assert.Empty(otherSession);
    }

    [Fact]
    public async Task ConversationAcrossSessionsAndStoreModesAsync()
    {
        // Arrange: one service-side conversation and three separate sessions. A fresh provider is built
        // for every call, the way the host does, so anything remembered between calls belongs to the
        // session and not to the provider object. A session only starts holding turns of its own once it
        // is used for an unstored one, and a session that never did starts from what the service saved.
        var service = new FakeStoredResponses();
        var sessionA = new ConversationSession();

        // 1. Stored turn on session A: nothing precedes it, and the service records it.
        Assert.Equal(["call 1"], await CallAsync(service, sessionA, store: true, "call 1"));

        // The other two sessions join the same conversation, so they start from the turn the service
        // last saved for it. Neither holds anything of its own yet.
        var sessionB = new ConversationSession { LastStoredResponseId = sessionA.LastStoredResponseId };
        var sessionC = new ConversationSession { LastStoredResponseId = sessionA.LastStoredResponseId };

        // 2. Unstored turn on session A: it reads the stored turn back and keeps its own turn in the session.
        Assert.Equal(["call 1", "reply 1", "call 2"], await CallAsync(service, sessionA, store: false, "call 2"));

        // 3. A different session starts from the last saved turn only: session A's unstored turn is held
        //    in session A and is invisible here.
        Assert.Equal(["call 1", "reply 1", "call 3"], await CallAsync(service, sessionB, store: false, "call 3"));

        // 4. Asking the service to store a turn in a session that is already holding unstored ones is
        //    refused: the service would record this turn without the turns that came before it.
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CallAsync(service, sessionB, store: true, "call 4"));
        Assert.Contains("were not stored", refused.Message, StringComparison.Ordinal);

        // 5. Continuing unstored in the same session is fine, and it keeps growing its own turns.
        Assert.Equal(
            ["call 1", "reply 1", "call 3", "reply 3", "call 5"],
            await CallAsync(service, sessionB, store: false, "call 5"));

        // 6. Still refused, for the same reason.
        await Assert.ThrowsAsync<InvalidOperationException>(() => CallAsync(service, sessionB, store: true, "call 6"));

        // 7. A session holding nothing of its own may store. It branches off the last turn the service
        //    saved, which is still the first one.
        Assert.Equal(["call 1", "reply 1", "call 7"], await CallAsync(service, sessionC, store: true, "call 7"));

        // 8. Session B is unaffected by the turn stored from session C: that turn sits on another branch
        //    of the conversation, so it is not among the turns leading to session B's last saved one.
        Assert.Equal(
            ["call 1", "reply 1", "call 3", "reply 3", "call 5", "reply 5", "call 8"],
            await CallAsync(service, sessionB, store: false, "call 8"));

        // 9. And session A still sees its own thread of the conversation.
        Assert.Equal(
            ["call 1", "reply 1", "call 2", "reply 2", "call 9"],
            await CallAsync(service, sessionA, store: false, "call 9"));
    }

    /// <summary>
    /// Runs one turn the way the host does: a new provider is built for the request, asked for the
    /// messages to send, and then told what the turn produced. Because the provider is new every time,
    /// whatever carries over between calls is held by the session it was given. Returns the message
    /// texts the model would have received.
    /// </summary>
    private static async Task<string[]> CallAsync(FakeStoredResponses service, ConversationSession session, bool store, string text)
    {
        var context = CreateContext(service.TurnsLeadingTo(session.LastStoredResponseId));
        var provider = new FoundryChatHistoryProvider(context, serviceStoresThisTurn: store);
        var agent = CreateAgent();

        var sent = (await provider.InvokingAsync(
            new ChatHistoryProvider.InvokingContext(agent, session.Session, [new ChatMessage(ChatRole.User, text)]),
            CancellationToken.None)).ToList();

        var reply = new ChatMessage(ChatRole.Assistant, text.Replace("call", "reply", StringComparison.Ordinal));
        await provider.InvokedAsync(
            new ChatHistoryProvider.InvokedContext(agent, session.Session, sent, [reply]),
            CancellationToken.None);

        if (store)
        {
            session.LastStoredResponseId = service.Store(session.LastStoredResponseId, [new ChatMessage(ChatRole.User, text), reply]);
        }

        return [.. sent.Select(m => m.Text)];
    }

    /// <summary>An agent session, plus the last turn the service saved for the conversation it follows.</summary>
    private sealed class ConversationSession
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
