// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.AgentServer.Core.Storage;
using Microsoft.Agents.AI.Foundry.Hosting;

namespace Microsoft.Agents.AI.Foundry.UnitTests.Hosting;

public sealed class FoundryAgentSessionStoreTests
{
    [Fact]
    public async Task SaveSessionAsync_ThenGetSessionAsync_RoundTripsAsync()
    {
        // Arrange
        var backing = new FakeStateStore();
        var store = NewStore(backing);
        var agent = new TestAgent("{\"foo\":7}", name: "Concierge");

        // Act
        await store.SaveSessionAsync(agent, "round-trip", new TestSession(), userId: "alice");
        var session = await store.GetSessionAsync(agent, "round-trip", userId: "alice");

        // Assert
        Assert.NotNull(session);
        Assert.Equal(1, agent.SerializeCalls);
        Assert.Equal(1, agent.DeserializeCalls);
        Assert.Equal(7, agent.LastDeserialized!.Value.GetProperty("foo").GetInt32());
    }

    [Fact]
    public async Task SaveSessionAsync_StoresReadableLogicalKeyAlongsideTheSessionAsync()
    {
        // Arrange
        var backing = new FakeStateStore();
        var store = NewStore(backing);
        var agent = new TestAgent(name: "Concierge");

        // Act
        await store.SaveSessionAsync(agent, "conv-1", new TestSession(), userId: "alice");

        // Assert: the item body keeps the readable key so a stored item can be traced back.
        var item = Assert.Single(backing.Items);
        Assert.Equal("\"a-Concierge:u-alice:c-conv-1\"", item["key"].ToString());
    }

    [Fact]
    public async Task GetSessionAsync_NothingStored_ReturnsNullAsync()
    {
        // Arrange
        var store = NewStore(new FakeStateStore());
        var agent = new TestAgent();

        // Act
        var session = await store.GetSessionAsync(agent, "conv-1", userId: null);

        // Assert
        Assert.Null(session);
        Assert.Equal(0, agent.CreateCalls);
        Assert.Equal(0, agent.DeserializeCalls);
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_NothingStored_ReturnsFreshSessionFromAgentAsync()
    {
        // Arrange
        var store = NewStore(new FakeStateStore());
        var agent = new TestAgent();

        // Act
        var session = await store.GetOrCreateSessionAsync(agent, "conv-1", userId: null);

        // Assert
        Assert.NotNull(session);
        Assert.Equal(1, agent.CreateCalls);
        Assert.Equal(0, agent.DeserializeCalls);
    }

    [Fact]
    public async Task GetSessionAsync_DifferentUser_DoesNotReadAnotherUsersSessionAsync()
    {
        // Arrange: Alice saves under the conversation id Bob will forge.
        var store = NewStore(new FakeStateStore());
        var agent = new TestAgent("{\"secret\":\"alice-only\"}", name: "Concierge");
        await store.SaveSessionAsync(agent, "shared-conv", new TestSession(), userId: "alice");

        // Act
        var bobSession = await store.GetSessionAsync(agent, "shared-conv", userId: "bob");

        // Assert
        Assert.Null(bobSession);
        Assert.Equal(0, agent.DeserializeCalls);
    }

    [Fact]
    public async Task GetSessionAsync_DifferentAgent_DoesNotReadAnotherAgentsSessionAsync()
    {
        // Arrange: one container hosts several keyed agents that must not collide on a shared id.
        var backing = new FakeStateStore();
        var store = NewStore(backing);
        var concierge = new TestAgent("{\"owner\":\"concierge\"}", name: "Concierge");
        var researcher = new TestAgent(name: "Researcher");
        await store.SaveSessionAsync(concierge, "shared-conv", new TestSession(), userId: "alice");

        // Act
        var otherSession = await store.GetSessionAsync(researcher, "shared-conv", userId: "alice");

        // Assert
        Assert.Null(otherSession);
        Assert.Equal(0, researcher.DeserializeCalls);
    }

    [Fact]
    public async Task GetStoreAsync_ResolvesTheStoreOnceAcrossManyCallsAsync()
    {
        // Arrange: binding the store costs a round trip, so it must not happen per request.
        var backing = new FakeStateStore();
        var bindCount = 0;
        var store = new FoundryAgentSessionStore(_ =>
        {
            Interlocked.Increment(ref bindCount);
            return Task.FromResult<FoundryStateStore>(backing);
        });
        var agent = new TestAgent(name: "Concierge");

        // Act
        await store.SaveSessionAsync(agent, "conv-1", new TestSession(), userId: null);
        await store.GetSessionAsync(agent, "conv-1", userId: null);
        await store.GetSessionAsync(agent, "conv-2", userId: null);

        // Assert
        Assert.Equal(1, bindCount);
    }

    [Fact]
    public async Task GetStoreAsync_FailedBinding_IsRetriedOnTheNextCallAsync()
    {
        // Arrange: a transient failure while binding must not disable the store for the process.
        var backing = new FakeStateStore();
        var attempts = 0;
        var store = new FoundryAgentSessionStore(_ =>
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<FoundryStateStore>(new FoundryStorageApiException(503, "transient"))
                : Task.FromResult<FoundryStateStore>(backing);
        });
        var agent = new TestAgent(name: "Concierge");

        // Act
        await Assert.ThrowsAsync<FoundryStorageApiException>(
            async () => await store.GetSessionAsync(agent, "conv-1", userId: null));
        var session = await store.GetSessionAsync(agent, "conv-1", userId: null);

        // Assert
        Assert.Null(session);
        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData("Concierge", "alice", "conv-1", "a-Concierge:u-alice:c-conv-1")]
    [InlineData("Concierge", null, "conv-1", "a-Concierge:c-conv-1")]
    [InlineData(null, "alice", "conv-1", "u-alice:c-conv-1")]
    [InlineData(null, null, "conv-1", "c-conv-1")]
    [InlineData("x", "x", "conv-1", "a-x:u-x:c-conv-1")]
    public void BuildLogicalKey_UsesTheSamePrefixSchemeAsTheOtherStores(string? agentName, string? userId, string conversationId, string expected)
    {
        // Arrange
        var agent = new TestAgent(name: agentName);

        // Act
        var key = FoundryAgentSessionStore.BuildLogicalKey(agent, conversationId, userId);

        // Assert
        Assert.Equal(expected, key);
    }

    [Fact]
    public void BuildItemKey_StaysWithinThePlatformKeyLimitForAnyInput()
    {
        // Arrange: an agent name plus a user id plus a conversation id can easily pass 128 chars.
        var logicalKey = FoundryAgentSessionStore.BuildLogicalKey(
            new TestAgent(name: new string('a', 200)),
            new string('c', 200),
            new string('u', 200));

        // Act
        var itemKey = FoundryAgentSessionStore.BuildItemKey(logicalKey);

        // Assert
        Assert.InRange(itemKey.Length, 1, 128);
    }

    [Fact]
    public void BuildItemKey_IsStableAndDistinctPerLogicalKey()
    {
        // Arrange / Act
        var first = FoundryAgentSessionStore.BuildItemKey("a-Concierge:u-alice:c-conv-1");
        var same = FoundryAgentSessionStore.BuildItemKey("a-Concierge:u-alice:c-conv-1");
        var other = FoundryAgentSessionStore.BuildItemKey("a-Concierge:u-bob:c-conv-1");

        // Assert
        Assert.Equal(first, same);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void Constructor_NullOrWhitespaceStoreName_Throws()
    {
        // Arrange
        var credential = new FakeCredential();

        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new FoundryAgentSessionStore(credential, storeName: null!));
        Assert.Throws<ArgumentException>(() => new FoundryAgentSessionStore(credential, storeName: "   "));
    }

    private static FoundryAgentSessionStore NewStore(FakeStateStore backing)
        => new(_ => Task.FromResult<FoundryStateStore>(backing));

    private sealed class FakeCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("token", DateTimeOffset.MaxValue);

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new Azure.Core.AccessToken("token", DateTimeOffset.MaxValue));
    }

    /// <summary>
    /// An in-memory stand-in for the platform state store. <see cref="FoundryStateStore"/> exposes a
    /// protected constructor and virtual members precisely so it can be substituted like this.
    /// </summary>
    private sealed class FakeStateStore : FoundryStateStore
    {
        private readonly ConcurrentDictionary<string, IDictionary<string, BinaryData>> _items = new(StringComparer.Ordinal);

        public IReadOnlyCollection<IDictionary<string, BinaryData>> Items => (IReadOnlyCollection<IDictionary<string, BinaryData>>)this._items.Values;

        public override string Name => FoundryAgentSessionStore.DefaultStoreName;

        public override Task<StateStoreItemRef> SetItemAsync(
            string key,
            IDictionary<string, BinaryData> value,
            IReadOnlyDictionary<string, string>? tags = null,
            string? ifMatch = null,
            bool requireExists = false,
            CancellationToken cancellationToken = default)
        {
            this._items[key] = value;
            return Task.FromResult(AzureAIAgentServerCoreStorageModelFactory.StateStoreItemRef(id: key, key: key, etag: "etag"));
        }

        public override Task<StateStoreItem?> GetItemAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(this._items.TryGetValue(key, out var value)
                ? AzureAIAgentServerCoreStorageModelFactory.StateStoreItem(id: key, key: key, value: value, etag: "etag")
                : null);
    }

    private sealed class TestSession : AgentSession
    {
    }

    private sealed class TestAgent : AIAgent
    {
        private readonly string _serializedJson;
        private readonly string? _name;

        public TestAgent(string serializedJson = "{}", string? name = null)
        {
            this._serializedJson = serializedJson;
            this._name = name;
        }

        public override string? Name => this._name;

        public int CreateCalls { get; private set; }
        public int SerializeCalls { get; private set; }
        public int DeserializeCalls { get; private set; }
        public JsonElement? LastDeserialized { get; private set; }

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        {
            this.CreateCalls++;
            return new ValueTask<AgentSession>(new TestSession());
        }

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
        {
            this.SerializeCalls++;
            using var doc = JsonDocument.Parse(this._serializedJson);
            return new ValueTask<JsonElement>(doc.RootElement.Clone());
        }

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
        {
            this.DeserializeCalls++;
            this.LastDeserialized = serializedState.Clone();
            return new ValueTask<AgentSession>(new TestSession());
        }

        protected override Task<AgentResponse> RunCoreAsync(IEnumerable<Extensions.AI.ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<Extensions.AI.ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
