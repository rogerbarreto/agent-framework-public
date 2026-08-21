// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.AgentServer.Responses;
using Azure.AI.AgentServer.Responses.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MeaiTextContent = Microsoft.Extensions.AI.TextContent;

namespace Microsoft.Agents.AI.Foundry.Hosting.UnitTests;

/// <summary>
/// Deterministic tests for the resilient (crash-recovery) behavior of
/// <see cref="AgentFrameworkResponseHandler"/>. They drive the handler with a fake agent that
/// records the messages it receives and a fake session store, so recovery semantics can be asserted
/// without a real model, a real process crash, or timing.
/// </summary>
public class AgentFrameworkResponseHandlerResilienceTests
{
    private const string ResponseId = "resp_0000000000000000000000000000000000000000000000";

    [Fact]
    public async Task CreateAsync_Recovery_WithoutPersistedSession_ReinjectsInputAsync()
    {
        // Arrange: recovery ran before the first AgentSession snapshot was persisted.
        var recording = new RecordingAgent();
        var handler = CreateHandler(recording, new InMemoryAgentSessionStore(), resilient: true);
        var request = NewBackgroundStoreRequest("original input");
        var context = CreateContext(isRecovery: true);

        // Act
        await CollectEventsAsync(handler, request, context);

        // Assert: no session exists to resume, so recovery must restart from the original input.
        Assert.NotNull(recording.LastMessages);
        Assert.Contains(
            recording.LastMessages!,
            message => message.Text.Contains("original input", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_Recovery_WithPersistedSession_DoesNotReinjectInputAsync()
    {
        // Arrange: a prior lifetime persisted an AgentSession for this response.
        var recording = new RecordingAgent();
        var store = new AlwaysLoadedSessionStore();
        var handler = CreateHandler(recording, store, resilient: true);
        var request = NewBackgroundStoreRequest("original input");
        var context = CreateContext(isRecovery: true);

        // Act
        await CollectEventsAsync(handler, request, context);

        // Assert: the restored session owns re-entry, so the original input is not duplicated.
        Assert.NotNull(recording.LastMessages);
        Assert.Empty(recording.LastMessages!);
    }

    [Fact]
    public async Task CreateAsync_FreshTurn_InjectsInputAsync()
    {
        // Arrange: the same request on a fresh (non-recovery) turn.
        var recording = new RecordingAgent();
        var handler = CreateHandler(recording, new InMemoryAgentSessionStore(), resilient: true);
        var request = NewBackgroundStoreRequest("original input");
        var context = CreateContext(isRecovery: false);

        // Act
        await CollectEventsAsync(handler, request, context);

        // Assert: a fresh turn feeds the request input to the agent.
        Assert.NotNull(recording.LastMessages);
        Assert.Contains(recording.LastMessages!, m => m.Text.Contains("original input", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_ResilientTurn_MidStreamSaveFailure_StillCompletesAsync()
    {
        // Arrange: a store whose first save throws, mimicking the serialize race that can happen
        // when the incremental (mid-stream) save runs while the workflow is still advancing. The
        // later end-of-turn save succeeds.
        var store = new ThrowOnceSessionStore();
        var recording = new RecordingAgent();
        var handler = CreateHandler(recording, store, resilient: true);
        var request = NewBackgroundStoreRequest("hello");
        var context = CreateContext(isRecovery: false);

        // Act
        var events = await CollectEventsAsync(handler, request, context);

        // Assert: the failed incremental save was swallowed and the turn still reached a completed
        // terminal event (it did not escape as a handler failure that leaves the response stuck).
        Assert.True(store.SaveAttempts >= 1, "Expected at least one session save attempt.");
        Assert.Contains(events, e => e is ResponseCompletedEvent);
        Assert.DoesNotContain(events, e => e is ResponseFailedEvent);
    }

    [Fact]
    public async Task CreateAsync_Recovery_UsesAvailablePersistedResponseAsStreamSeedAsync()
    {
        // Arrange: AgentServer supplied a durable snapshot that happens to contain two output items.
        // The handler does not create this checkpoint; this test verifies how it consumes a snapshot
        // when one is available.
        var persisted = new ResponseObject("resp_" + new string('0', 46), "test");
        persisted.Output.Add(NewMessageItem("prior_1", "prior item one"));
        persisted.Output.Add(NewMessageItem("prior_2", "prior item two"));

        var recording = new RecordingAgent();
        var handler = CreateHandler(recording, new InMemoryAgentSessionStore(), resilient: true);
        var request = NewBackgroundStoreRequest("input");
        var context = CreateContext(isRecovery: true, persistedResponse: persisted);

        // Act
        var events = await CollectEventsAsync(handler, request, context);

        // Assert: new items start after the output watermark carried by the available snapshot. The
        // handler does not treat that watermark as the workflow checkpoint or re-emit seeded items.
        var addedIndexes = events.OfType<ResponseOutputItemAddedEvent>().Select(e => e.OutputIndex).ToList();
        Assert.NotEmpty(addedIndexes);
        Assert.All(addedIndexes, i => Assert.True(i >= 2, $"New output item index {i} collided with a seeded item (0 or 1)."));

        // The final response retains the two items supplied by AgentServer and appends the newly
        // emitted item. This does not assert that normal workflow recovery produces such a snapshot.
        var completed = events.OfType<ResponseCompletedEvent>().Single();
        Assert.Equal(3, completed.Response.Output.Count);
    }

    [Fact]
    public async Task CreateAsync_ShutdownAfterAgentAdvanced_DoesNotSaveUnemittedSessionStateAsync()
    {
        // Arrange: the agent advances its session and returns an update after shutdown is visible.
        var store = new CountingSessionStore();
        var handler = CreateHandler(
            new SessionAdvancingAgent(),
            store,
            resilient: true);
        var request = NewBackgroundStoreRequest("input");
        var context = CreateContext(isRecovery: false, shutdownRequested: true);

        // Act
        var events = await CollectEventsAsync(handler, request, context);

        // Assert: only the lifecycle prefix was emitted, so advanced session state must not be saved.
        Assert.DoesNotContain(events, responseEvent => responseEvent is ResponseOutputItemDoneEvent);
        Assert.Equal(0, store.SaveAttempts);
    }

    [Fact]
    public void Constructor_ExistingThreeParameterSignature_IsPreserved()
    {
        // Act
        var constructor = typeof(AgentFrameworkResponseHandler).GetConstructor(
            [
                typeof(IServiceProvider),
                typeof(ILogger<AgentFrameworkResponseHandler>),
                typeof(FoundryToolboxService),
            ]);

        // Assert
        Assert.NotNull(constructor);
    }

    private static AgentFrameworkResponseHandler CreateHandler(AIAgent agent, AgentSessionStore store, bool resilient)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(agent);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new FoundryResponsesOptions { ResilientBackground = resilient });
        return new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance, toolboxService: null, foundryResponsesOptions: options);
    }

    private static CreateResponse NewBackgroundStoreRequest(string text)
    {
        var request = new CreateResponse { Model = "test", Background = true, Store = true };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new
            {
                type = "message",
                id = "msg_in_1",
                status = "completed",
                role = "user",
                content = new[] { new { type = "input_text", text } }
            }
        });
        return request;
    }

    private static ResponseContext CreateContext(
        bool isRecovery,
        ResponseObject? persistedResponse = null,
        bool shutdownRequested = false)
    {
        var mock = new Mock<ResponseContext>(ResponseId) { CallBase = true };
        mock.Setup(x => x.IsRecovery).Returns(isRecovery);
        mock.Setup(x => x.PersistedResponse).Returns(persistedResponse);
        mock.Setup(x => x.ExitForRecoveryAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mock.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());
        if (shutdownRequested)
        {
            mock.Object.IsShutdownRequested = true;
        }

        return mock.Object;
    }

    private static OutputItemMessage NewMessageItem(string id, string text) =>
        new(
            id: id,
            role: MessageRole.Assistant,
            content: [new MessageContentOutputTextContent(text, Array.Empty<Annotation>(), Array.Empty<LogProb>())],
            status: MessageStatus.Completed);

    private static async Task<List<ResponseStreamEvent>> CollectEventsAsync(
        AgentFrameworkResponseHandler handler,
        CreateResponse request,
        ResponseContext context)
    {
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, context, CancellationToken.None))
        {
            events.Add(evt);
        }

        return events;
    }

    /// <summary>
    /// A fake agent that records the messages passed to each run so a test can assert exactly what
    /// the handler fed it (for example, that recovery injected nothing).
    /// </summary>
    private sealed class RecordingAgent : AIAgent
    {
        public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

        protected override string? IdCore => "recording-agent";

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            this.LastMessages = messages.ToList();
            yield return new AgentResponseUpdate
            {
                MessageId = "msg_rec_1",
                Contents = [new MeaiTextContent("recorded")]
            };
            await Task.CompletedTask;
        }

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
            new(new RecordingSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default) =>
            new(JsonSerializer.SerializeToElement(new { }, jsonSerializerOptions));

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default) =>
            new(new RecordingSession());

        private sealed class RecordingSession : AgentSession
        {
            public RecordingSession()
            {
            }
        }
    }

    /// <summary>
    /// A fake session store whose first <see cref="SaveSessionAsync"/> throws (mimicking the
    /// serialize race), then succeeds, while loads always create a fresh session.
    /// </summary>
    private sealed class ThrowOnceSessionStore : AgentSessionStore
    {
        private int _saveAttempts;

        public int SaveAttempts => this._saveAttempts;

        public override ValueTask SaveSessionAsync(AIAgent agent, string conversationId, AgentSession session, string? userId, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref this._saveAttempts);
            if (attempt == 1)
            {
                throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
            }

            return default;
        }

        public override async ValueTask<AgentSession?> GetSessionAsync(AIAgent agent, string conversationId, string? userId, CancellationToken cancellationToken = default) =>
            await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class CountingSessionStore : AgentSessionStore
    {
        private int _saveAttempts;

        public int SaveAttempts => this._saveAttempts;

        public override ValueTask SaveSessionAsync(
            AIAgent agent,
            string conversationId,
            AgentSession session,
            string? userId,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref this._saveAttempts);
            return default;
        }

        public override ValueTask<AgentSession?> GetSessionAsync(
            AIAgent agent,
            string conversationId,
            string? userId,
            CancellationToken cancellationToken = default) =>
            new((AgentSession?)null);
    }

    private sealed class AlwaysLoadedSessionStore : AgentSessionStore
    {
        public override ValueTask SaveSessionAsync(
            AIAgent agent,
            string conversationId,
            AgentSession session,
            string? userId,
            CancellationToken cancellationToken = default) =>
            default;

        public override async ValueTask<AgentSession?> GetSessionAsync(
            AIAgent agent,
            string conversationId,
            string? userId,
            CancellationToken cancellationToken = default) =>
            await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class SessionAdvancingAgent : AIAgent
    {
        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var advancingSession = Assert.IsType<AdvancingSession>(session);
            advancingSession.Phase = 1;
            yield return new AgentResponseUpdate
            {
                MessageId = "msg_shutdown_1",
                Contents = [new MeaiTextContent("not emitted")]
            };
            await Task.CompletedTask;
        }

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(
            CancellationToken cancellationToken = default) =>
            new(new AdvancingSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default)
        {
            var advancingSession = Assert.IsType<AdvancingSession>(session);
            return new(JsonSerializer.SerializeToElement(
                new { advancingSession.Phase },
                jsonSerializerOptions));
        }

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default) =>
            new(new AdvancingSession
            {
                Phase = serializedState.GetProperty("Phase").GetInt32(),
            });

        private sealed class AdvancingSession : AgentSession
        {
            public int Phase { get; set; }
        }
    }
}
