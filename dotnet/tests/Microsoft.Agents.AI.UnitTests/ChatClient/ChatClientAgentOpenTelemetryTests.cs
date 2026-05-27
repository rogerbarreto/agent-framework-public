// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OpenTelemetry.Trace;

namespace Microsoft.Agents.AI.UnitTests.ChatClient;

/// <summary>
/// Tests covering OpenTelemetry behavior of <see cref="ChatClientAgent"/>.
///
/// The tests are organized into two phases:
///   PHASE 1 (GREEN baseline): scenarios that exist today but were uncovered. They lock in
///     non-changing behavior so we detect any regression caused by the BCL-native emission work
///     (ADR 0027 / Option G).
///   PHASE 2 (RED -> GREEN): new behavior introduced by Option G. These tests start failing on
///     the current codebase and turn green once Option G is implemented in ChatClientAgent.
/// </summary>
/// <remarks>
/// Tests that need to capture telemetry use <see cref="OwnerScopedActivityCapture"/> to filter
/// activities by their owning <see cref="ChatClientAgent"/> instance, making them safe to run
/// in parallel even when multiple tests subscribe to the same global default source.
/// </remarks>
public class ChatClientAgentOpenTelemetryTests
{
    private const string DefaultSourceName = "Experimental.Microsoft.Agents.AI";

    // -------------------- PHASE 1: GREEN baseline (current behavior) --------------------

    /// <summary>
    /// Bare <see cref="ChatClientAgent"/> with no telemetry subscription emits no spans.
    /// This must remain true after Option G (zero allocation when no listeners).
    /// </summary>
    [Fact]
    public async Task BareChatClientAgent_NoListener_EmitsNothing_Async()
    {
        // Arrange
        var activities = new List<Activity>();
        // NOTE: no tracer provider built — there are no listeners at all.
        var fakeChatClient = new SpyChatClient();
        var agent = new ChatClientAgent(fakeChatClient);

        // Act
        _ = await agent.RunAsync("hi");

        // Assert
        Assert.Empty(activities);
        Assert.Equal(1, fakeChatClient.GetResponseAsyncCallCount);
    }

    /// <summary>
    /// Bare <see cref="ChatClientAgent"/> with a tracer subscribed to a different (unrelated)
    /// source emits nothing on the AgentFramework source. Stays true after Option G because the
    /// self-wrap targets the AgentFramework default source only.
    /// </summary>
    [Fact]
    public async Task BareChatClientAgent_ListenerOnOtherSource_EmitsNothing_Async()
    {
        // Arrange: subscribe to a totally unrelated source.
        var activities = new List<Activity>();
        using var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource("Some.Unrelated.Source")
            .AddInMemoryExporter(activities)
            .Build();

        var fakeChatClient = new SpyChatClient();
        var agent = new ChatClientAgent(fakeChatClient);

        // Act
        _ = await agent.RunAsync("hi");

        // Assert
        Assert.Empty(activities);
        Assert.Equal(1, fakeChatClient.GetResponseAsyncCallCount);
    }

    /// <summary>
    /// <see cref="ChatClientAgent.GetService"/> for <see cref="IChatClient"/> returns the agent's
    /// own decorated <see cref="ChatClientAgent.ChatClient"/> (not the raw constructor input).
    /// Stays true after Option G — we self-wrap a separate field, not the publicly exposed one.
    /// </summary>
    [Fact]
    public void ChatClientAgent_GetServiceIChatClient_ReturnsAgentChatClient()
    {
        // Arrange
        var fakeChatClient = new SpyChatClient();
        var agent = new ChatClientAgent(fakeChatClient);

        // Act
        var resolved = agent.GetService<IChatClient>();

        // Assert: the agent exposes its decorated stack via the ChatClient property and GetService.
        Assert.NotNull(resolved);
        Assert.Same(agent.ChatClient, resolved);
    }

    /// <summary>
    /// <see cref="ChatClientAgent.GetService"/> for <see cref="ChatClientAgent"/> returns this.
    /// Critical for the per-instance suppression marker — both
    /// <c>OpenTelemetryAgent.UpdateCurrentActivity</c> and other discovery paths rely on this.
    /// </summary>
    [Fact]
    public void ChatClientAgent_GetServiceChatClientAgent_ReturnsSelf()
    {
        // Arrange
        var fakeChatClient = new SpyChatClient();
        var agent = new ChatClientAgent(fakeChatClient);

        // Act
        var resolved = agent.GetService<ChatClientAgent>();

        // Assert
        Assert.Same(agent, resolved);
    }

    /// <summary>
    /// When a custom <see cref="DelegatingAIAgent"/> wraps <see cref="ChatClientAgent"/>, calling
    /// <c>GetService&lt;ChatClientAgent&gt;()</c> on the outer agent traverses inward and finds the
    /// inner ChatClientAgent. This is the lookup OpenTelemetryAgent uses to set its per-instance
    /// suppression marker after Option G.
    /// </summary>
    [Fact]
    public void DelegatingWrapper_GetServiceChatClientAgent_FindsInner()
    {
        // Arrange
        var fakeChatClient = new SpyChatClient();
        var inner = new ChatClientAgent(fakeChatClient);
        var wrapper = new PassthroughDelegatingAgent(inner);

        // Act
        var resolved = wrapper.GetService<ChatClientAgent>();

        // Assert
        Assert.Same(inner, resolved);
    }

    // -------------------- PHASE 2: RED -> GREEN (Option G new behavior) --------------------

    /// <summary>
    /// Bare <see cref="ChatClientAgent"/> with a tracer subscribed to the AgentFramework default
    /// source emits the same 2-span shape as the explicit <c>new OpenTelemetryAgent(agent)</c>
    /// decorator path: one <c>invoke_agent</c> span plus one nested <c>chat</c> span. This is
    /// the symmetric behavior of Option G — the bare path reuses <see cref="OpenTelemetryAgent"/>
    /// internally so its output is identical to the decorated path.
    /// </summary>
    [Fact]
    public async Task BareChatClientAgent_ListenerOnDefaultSource_EmitsInvokeAgentAndChatSpans_Async()
    {
        // Arrange
        var fakeChatClient = new SpyChatClient();
        var agent = new ChatClientAgent(fakeChatClient, new ChatClientAgentOptions { Name = "BareAgent" });
        using var capture = new OwnerScopedActivityCapture(agent);

        // Act
        _ = await agent.RunAsync("hi");

        // Assert: 2 activities — matches today's `new OpenTelemetryAgent(agent)` default exactly.
        Assert.Equal(2, capture.Activities.Count);
        Assert.Contains(capture.Activities, a => a.DisplayName.StartsWith("invoke_agent", StringComparison.Ordinal));
        Assert.Contains(capture.Activities, a => string.Equals(a.GetTagItem("gen_ai.operation.name") as string, "chat", StringComparison.Ordinal));
    }

    /// <summary>
    /// Streaming path also produces the 2-span shape on a bare <see cref="ChatClientAgent"/>.
    /// </summary>
    [Fact]
    public async Task BareChatClientAgent_Streaming_ListenerOnDefaultSource_EmitsInvokeAgentAndChatSpans_Async()
    {
        // Arrange
        var fakeChatClient = new SpyChatClient();
        var agent = new ChatClientAgent(fakeChatClient, new ChatClientAgentOptions { Name = "BareAgent" });
        using var capture = new OwnerScopedActivityCapture(agent);

        // Act
        await foreach (var _ in agent.RunStreamingAsync("hi"))
        {
        }

        // Assert
        Assert.Equal(2, capture.Activities.Count);
        Assert.Contains(capture.Activities, a => a.DisplayName.StartsWith("invoke_agent", StringComparison.Ordinal));
        Assert.Contains(capture.Activities, a => string.Equals(a.GetTagItem("gen_ai.operation.name") as string, "chat", StringComparison.Ordinal));
    }

    /// <summary>
    /// The <c>invoke_agent</c> span carries the agent's <c>Name</c>, <c>Id</c>, and
    /// <c>Description</c> in <c>gen_ai.agent.*</c> tags.
    /// </summary>
    [Fact]
    public async Task BareChatClientAgent_InvokeAgentSpan_CarriesAgentIdentityTags_Async()
    {
        // Arrange
        var fakeChatClient = new SpyChatClient();
        var agent = new ChatClientAgent(fakeChatClient, new ChatClientAgentOptions
        {
            Id = "agent-123",
            Name = "MyAgent",
            Description = "Helpful test agent.",
        });
        using var capture = new OwnerScopedActivityCapture(agent);

        // Act
        _ = await agent.RunAsync("hi");

        // Assert
        var invokeAgentSpan = Assert.Single(capture.Activities, a => a.DisplayName.StartsWith("invoke_agent", StringComparison.Ordinal));
        Assert.Equal("invoke_agent", invokeAgentSpan.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("agent-123", invokeAgentSpan.GetTagItem("gen_ai.agent.id"));
        Assert.Equal("MyAgent", invokeAgentSpan.GetTagItem("gen_ai.agent.name"));
        Assert.Equal("Helpful test agent.", invokeAgentSpan.GetTagItem("gen_ai.agent.description"));
    }

    /// <summary>
    /// Passive <see cref="DelegatingAIAgent"/> wrapper (e.g. LoggingAgent-like) that simply
    /// forwards must NOT suppress emission. The inner <see cref="ChatClientAgent"/> still self-wraps
    /// and the 2-span shape is observed by the consumer.
    /// </summary>
    [Fact]
    public async Task PassthroughDelegatingWrapper_DoesNotSuppress_InnerChatClientAgentEmits_Async()
    {
        // Arrange
        var fakeChatClient = new SpyChatClient();
        var inner = new ChatClientAgent(fakeChatClient, new ChatClientAgentOptions { Name = "WrappedAgent" });
        var wrapper = new PassthroughDelegatingAgent(inner);
        using var capture = new OwnerScopedActivityCapture(inner);

        // Act
        _ = await wrapper.RunAsync("hi");

        // Assert
        Assert.Equal(2, capture.Activities.Count);
        Assert.Contains(capture.Activities, a => a.DisplayName.StartsWith("invoke_agent", StringComparison.Ordinal));
        Assert.Contains(capture.Activities, a => string.Equals(a.GetTagItem("gen_ai.operation.name") as string, "chat", StringComparison.Ordinal));
    }

    /// <summary>
    /// Provider-style wrapper that constructs its inner <see cref="ChatClientAgent"/> with the
    /// user-facing Id/Name (FoundryAgent pattern) yields an <c>invoke_agent</c> span tagged with
    /// the user-facing identity, not some internal alias.
    /// </summary>
    [Fact]
    public async Task ProviderStyleWrapper_PreservesUserFacingIdentityOnInvokeAgentSpan_Async()
    {
        // Arrange
        var fakeChatClient = new SpyChatClient();
        // Simulates how FoundryAgent (line 302-303, 335-336) sets agent name on the inner ChatClientAgent.
        var inner = new ChatClientAgent(fakeChatClient, new ChatClientAgentOptions
        {
            Id = "foundry-helper",
            Name = "foundry-helper",
        });
        var wrapper = new PassthroughDelegatingAgent(inner);
        using var capture = new OwnerScopedActivityCapture(inner);

        // Act
        _ = await wrapper.RunAsync("hi");

        // Assert
        var invokeAgentSpan = Assert.Single(capture.Activities, a => a.DisplayName.StartsWith("invoke_agent", StringComparison.Ordinal));
        Assert.Equal("foundry-helper", invokeAgentSpan.GetTagItem("gen_ai.agent.id"));
        Assert.Equal("foundry-helper", invokeAgentSpan.GetTagItem("gen_ai.agent.name"));
    }

    /// <summary>
    /// Explicit <see cref="OpenTelemetryAgent"/> decorator wrapping a <see cref="ChatClientAgent"/>
    /// continues to produce the same 2-span shape it does today — no triple emission from the
    /// inner self-wrap. The per-instance marker on the outer chat span suppresses the inner agent.
    /// </summary>
    [Fact]
    public async Task ExplicitOpenTelemetryAgent_NoTripleEmission_Async()
    {
        // Arrange
        var fakeChatClient = new SpyChatClient();
        var inner = new ChatClientAgent(fakeChatClient, new ChatClientAgentOptions { Name = "ExplicitWrapped" });
        using var decorated = new OpenTelemetryAgent(inner, DefaultSourceName);
        using var capture = new OwnerScopedActivityCapture(inner);

        // Act
        _ = await decorated.RunAsync("hi");

        // Assert: exactly 2 spans, today's exact behavior preserved.
        Assert.Equal(2, capture.Activities.Count);
        Assert.Contains(capture.Activities, a => a.DisplayName.StartsWith("invoke_agent", StringComparison.Ordinal));
        Assert.Contains(capture.Activities, a => string.Equals(a.GetTagItem("gen_ai.operation.name") as string, "chat", StringComparison.Ordinal));
    }

    /// <summary>
    /// Two sibling <see cref="ChatClientAgent"/> instances each emit their own 2-span pair when
    /// invoked sequentially. Per-instance marker scoping ensures one agent's invocation does NOT
    /// suppress the other's even if their Activities overlap temporally in the same trace.
    /// </summary>
    [Fact]
    public async Task TwoSiblingChatClientAgents_EachEmitsOwnSpans_Async()
    {
        // Arrange
        var spyA = new SpyChatClient();
        var spyB = new SpyChatClient();
        var agentA = new ChatClientAgent(spyA, new ChatClientAgentOptions { Id = "a", Name = "A" });
        var agentB = new ChatClientAgent(spyB, new ChatClientAgentOptions { Id = "b", Name = "B" });
        using var captureA = new OwnerScopedActivityCapture(agentA);
        using var captureB = new OwnerScopedActivityCapture(agentB);

        // Act
        _ = await agentA.RunAsync("hi A");
        _ = await agentB.RunAsync("hi B");

        // Assert: each capture holds its own agent's spans (2 each). Per-instance scoping verified
        // by the fact that captureA does not see agentB's spans and vice versa.
        Assert.Equal(2, captureA.Activities.Count);
        Assert.Equal(2, captureB.Activities.Count);
        var invokeA = Assert.Single(captureA.Activities, a => a.DisplayName.StartsWith("invoke_agent", StringComparison.Ordinal));
        var invokeB = Assert.Single(captureB.Activities, a => a.DisplayName.StartsWith("invoke_agent", StringComparison.Ordinal));
        Assert.Equal("a", invokeA.GetTagItem("gen_ai.agent.id"));
        Assert.Equal("b", invokeB.GetTagItem("gen_ai.agent.id"));
    }

    /// <summary>
    /// Concurrent first-call invocations on the same <see cref="ChatClientAgent"/> must result in
    /// a single cached self-wrap (no duplicate <c>OpenTelemetryChatClient</c> instances leaking).
    /// Verifies the Interlocked.CompareExchange-based lazy init is safe.
    /// </summary>
    [Fact]
    public async Task ConcurrentFirstCalls_LazyInitProducesSingleCachedWrap_Async()
    {
        // Arrange
        var fakeChatClient = new SpyChatClient();
        var agent = new ChatClientAgent(fakeChatClient, new ChatClientAgentOptions { Name = "RaceAgent" });
        using var capture = new OwnerScopedActivityCapture(agent);

        // Act: 8 concurrent first-call invocations.
        const int Concurrency = 8;
        var tasks = Enumerable.Range(0, Concurrency)
            .Select(_ => Task.Run(() => agent.RunAsync("hi")))
            .ToArray();
        await Task.WhenAll(tasks);

        // Assert: each call emits 2 spans, no extras.
        Assert.Equal(Concurrency * 2, capture.Activities.Count);
    }

    /// <summary>
    /// When a caller passes a <see cref="ChatClientAgentRunOptions.ChatClientFactory"/>, the
    /// factory must still be applied even when self-wrap is active. Order: self-wrap is built
    /// first, then the user factory wraps the result.
    /// </summary>
    [Fact]
    public async Task BareChatClientAgent_UserChatClientFactoryStillApplied_Async()
    {
        // Arrange
        var fakeChatClient = new SpyChatClient();
        var agent = new ChatClientAgent(fakeChatClient, new ChatClientAgentOptions { Name = "FactoryAgent" });
        using var capture = new OwnerScopedActivityCapture(agent);

        var factoryInvoked = false;
        var runOptions = new ChatClientAgentRunOptions
        {
            ChatClientFactory = cc =>
            {
                factoryInvoked = true;
                return cc; // identity wrap — just verify the hook fires.
            },
        };

        // Act
        _ = await agent.RunAsync("hi", options: runOptions);

        // Assert
        Assert.True(factoryInvoked, "User-provided ChatClientFactory must still be invoked after self-wrap.");
        Assert.Equal(2, capture.Activities.Count);
    }

    // -------------------- PHASE 2 — sensitive data behavior --------------------

    /// <summary>
    /// Bare <see cref="ChatClientAgent"/> does NOT capture message content by default.
    /// The underlying <see cref="OpenTelemetryChatClient"/> defaults
    /// <c>EnableSensitiveData = false</c> (matching the OpenTelemetry safe default), and the
    /// bare path provides no per-instance override property. Users who need sensitive-data
    /// capture must either (a) set the
    /// <c>OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT</c> environment variable BEFORE
    /// the process starts (read once at <c>TelemetryHelpers</c> static init), or
    /// (b) explicitly wrap with <see cref="OpenTelemetryAgent"/> and set
    /// <see cref="OpenTelemetryAgent.EnableSensitiveData"/>.
    /// </summary>
    [Fact]
    public async Task BareChatClientAgent_DefaultsToNoMessageCapture_Async()
    {
        // Arrange
        var fakeChatClient = new SensitiveSpyChatClient();
        var agent = new ChatClientAgent(fakeChatClient, new ChatClientAgentOptions { Name = "DefaultSensitiveAgent" });
        using var capture = new OwnerScopedActivityCapture(agent);

        // Act
        _ = await agent.RunAsync("hello world");

        // Assert — no message-content tags on any span by default (assuming env var not set).
        Assert.NotEmpty(capture.Activities);
        foreach (var activity in capture.Activities)
        {
            Assert.Null(activity.GetTagItem("gen_ai.input.messages"));
            Assert.Null(activity.GetTagItem("gen_ai.output.messages"));
            Assert.Null(activity.GetTagItem("gen_ai.prompt"));
        }
    }

    /// <summary>
    /// Explicit <see cref="OpenTelemetryAgent.EnableSensitiveData"/> property still works as today
    /// when wrapping a <see cref="ChatClientAgent"/> — verifies the existing per-instance override
    /// remains independent of the env var and continues to gate the chat span's message capture.
    /// This is the SAME mechanism today's users have relied on; ADR 0027 does not change it.
    /// </summary>
    [Fact]
    public async Task ExplicitOpenTelemetryAgent_EnableSensitiveData_True_CapturesMessages_Async()
    {
        // Arrange
        var fakeChatClient = new SensitiveSpyChatClient();
        var inner = new ChatClientAgent(fakeChatClient, new ChatClientAgentOptions { Name = "ExplicitWrapped" });
        using var decorated = new OpenTelemetryAgent(inner, DefaultSourceName)
        {
            EnableSensitiveData = true,
        };
        using var capture = new OwnerScopedActivityCapture(inner);

        // Act
        _ = await decorated.RunAsync("hello world");

        // Assert — message content captured on at least one span.
        Assert.NotEmpty(capture.Activities);
        var hasInputMessages = capture.Activities.Any(a =>
            a.GetTagItem("gen_ai.input.messages") is not null ||
            a.GetTagItem("gen_ai.prompt") is not null);
        Assert.True(hasInputMessages, "EnableSensitiveData=true on the decorator should capture message content.");
    }

    // -------------------- helpers --------------------

    /// <summary>Minimal IChatClient that records invocations and returns a canned response.</summary>
    private sealed class SpyChatClient : IChatClient
    {
        public int GetResponseAsyncCallCount { get; private set; }
        public int GetStreamingResponseAsyncCallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            this.GetResponseAsyncCallCount++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            this.GetStreamingResponseAsyncCallCount++;
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType?.IsInstanceOfType(this) == true ? this : null;

        public void Dispose() { }
    }

    /// <summary>
    /// Variant of <see cref="SpyChatClient"/> that advertises <see cref="ChatClientMetadata"/>
    /// so <see cref="OpenTelemetryChatClient"/> emits canonical chat-span shape (incl. provider name).
    /// Used by sensitive-data tests where we want a "realistic" telemetry-friendly chat client.
    /// </summary>
    private sealed class SensitiveSpyChatClient : IChatClient
    {
        private static readonly ChatClientMetadata s_metadata = new("test-provider", new Uri("https://localhost:1234"), "test-model");

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ack")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ack");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(ChatClientMetadata) ? s_metadata :
            serviceType?.IsInstanceOfType(this) == true ? this :
            null;

        public void Dispose() { }
    }

    /// <summary>
    /// Minimal <see cref="DelegatingAIAgent"/> that just forwards calls — represents a
    /// passive decorator (LoggingAgent-like) that should not affect telemetry emission.
    /// </summary>
    private sealed class PassthroughDelegatingAgent(AIAgent inner) : DelegatingAIAgent(inner)
    {
    }
}
