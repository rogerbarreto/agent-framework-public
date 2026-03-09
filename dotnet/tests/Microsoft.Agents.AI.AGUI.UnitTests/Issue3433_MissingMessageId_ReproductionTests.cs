// Copyright (c) Microsoft. All rights reserved.

// =============================================================================
// REPRODUCTION: Issue #3433 - ChatClientAgent streaming responses missing MessageId
// =============================================================================
//
// This file reproduces the bug described in:
// https://github.com/microsoft/agent-framework/issues/3433
//
// Problem:
//   When using ChatClientAgent with LLM providers that don't supply MessageId
//   on ChatResponseUpdate (e.g., Google GenAI/Vertex AI), the AGUI pipeline
//   emits events with "messageId": null. CopilotKit rejects these with a
//   Zod validation error: "Expected string, received null".
//
// Root Cause:
//   1. ChatClientAgent.RunCoreStreamingAsync copies MessageId from the
//      underlying ChatResponseUpdate — but if the provider didn't set it,
//      it stays null.
//   2. AsChatResponseUpdate() returns the original ChatResponseUpdate from
//      RawRepresentation directly, ignoring any MessageId set on the
//      AgentResponseUpdate wrapper.
//
// How to run:
//   dotnet test --filter "FullyQualifiedName~Issue3433" dotnet/tests/Microsoft.Agents.AI.AGUI.UnitTests
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.AGUI;
using Microsoft.Agents.AI.AGUI.Shared;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.AGUI.UnitTests;

/// <summary>
/// Reproduction tests for Issue #3433: ChatClientAgent streaming responses missing MessageId.
/// These tests demonstrate how null MessageId from an LLM provider propagates through
/// the pipeline and results in invalid AGUI events.
/// </summary>
public sealed class Issue3433_MissingMessageId_ReproductionTests
{
    // =========================================================================
    // Scenario 1: Text streaming with null MessageId
    // =========================================================================

    /// <summary>
    /// Reproduces the core bug: when ChatResponseUpdate objects have null MessageId,
    /// the AGUI events emitted by AsAGUIEventStreamAsync also have null/empty messageId.
    ///
    /// This is what happens when using Google GenAI or any provider that doesn't set
    /// MessageId on streaming responses.
    /// </summary>
    [Fact]
    public async Task Issue3433_TextStreaming_NullMessageId_ProducesInvalidAGUIEvents()
    {
        // Arrange - Simulate an LLM provider that does NOT set MessageId
        // (e.g., Google GenAI / Vertex AI)
        List<ChatResponseUpdate> providerUpdates =
        [
            new ChatResponseUpdate(ChatRole.Assistant, "Hello")
            {
                // MessageId is intentionally NOT set — this is the bug trigger
            },
            new ChatResponseUpdate(ChatRole.Assistant, " world")
            {
                // MessageId is intentionally NOT set
            },
            new ChatResponseUpdate(ChatRole.Assistant, "!")
            {
                // MessageId is intentionally NOT set
            }
        ];

        // Act - Pipe through the AGUI event stream (same pipeline used in production)
        List<BaseEvent> aguiEvents = [];
        await foreach (BaseEvent evt in providerUpdates.ToAsyncEnumerableAsync()
            .AsAGUIEventStreamAsync("thread-1", "run-1", AGUIJsonSerializerContext.Default.Options))
        {
            aguiEvents.Add(evt);
        }

        // Assert - Inspect the AGUI events for messageId validity
        //
        // BUG: When MessageId is null, AsAGUIEventStreamAsync's message-start check:
        //   !string.Equals(currentMessageId, chatResponse.MessageId)
        // evaluates to !string.Equals(null, null) → false on all chunks.
        // So TextMessageStartEvent is NEVER emitted, and text content is silently dropped.
        //
        // This means the entire text response is lost — even worse than a null messageId.

        List<TextMessageStartEvent> startEvents = aguiEvents.OfType<TextMessageStartEvent>().ToList();
        List<TextMessageContentEvent> contentEvents = aguiEvents.OfType<TextMessageContentEvent>().ToList();

        // BUG: No TextMessageStartEvent is emitted because null == null prevents the start logic
        Assert.NotEmpty(startEvents); // FAILS — zero start events emitted

        // BUG: No TextMessageContentEvent is emitted because the guard depends on currentMessageId
        Assert.NotEmpty(contentEvents); // FAILS — zero content events emitted
    }

    // =========================================================================
    // Scenario 2: Full ChatClientAgent pipeline with null MessageId
    // =========================================================================

    /// <summary>
    /// Reproduces the full pipeline: ChatClientAgent → AsChatResponseUpdatesAsync → AsAGUIEventStreamAsync.
    /// This mimics the exact flow used in AGUIEndpointRouteBuilderExtensions.MapAGUI().
    ///
    /// The mock IChatClient simulates a provider (like Google GenAI) that returns
    /// ChatResponseUpdate objects without MessageId.
    /// </summary>
    [Fact]
    public async Task Issue3433_FullPipeline_ChatClientAgent_ToAGUI_NullMessageId()
    {
        // Arrange - Create a ChatClientAgent with a mock chat client that
        // returns streaming updates WITHOUT MessageId (simulating Google GenAI)
        IChatClient mockChatClient = new NullMessageIdChatClient();
        ChatClientAgent agent = new(mockChatClient, name: "test-agent");

        ChatMessage userMessage = new(ChatRole.User, "tell me about agents");

        // Act - Run the full pipeline exactly as MapAGUI does:
        //   agent.RunStreamingAsync() → .AsChatResponseUpdatesAsync() → .AsAGUIEventStreamAsync()
        List<BaseEvent> aguiEvents = [];
        await foreach (BaseEvent evt in agent
            .RunStreamingAsync([userMessage])
            .AsChatResponseUpdatesAsync()
            .AsAGUIEventStreamAsync("thread-1", "run-1", AGUIJsonSerializerContext.Default.Options))
        {
            aguiEvents.Add(evt);
        }

        // Assert — The pipeline should produce AGUI events with valid messageId
        // BUG: Same as Scenario 1 — since ChatClientAgent doesn't set MessageId
        // and the mock provider returns null, AsAGUIEventStreamAsync silently drops
        // all text content (null == null means "same message" so no start event).
        List<TextMessageStartEvent> startEvents = aguiEvents.OfType<TextMessageStartEvent>().ToList();
        List<TextMessageContentEvent> contentEvents = aguiEvents.OfType<TextMessageContentEvent>().ToList();

        // BUG: No text events are emitted at all
        Assert.NotEmpty(startEvents);
        Assert.NotEmpty(contentEvents);

        // BUG: All messageId values will be null/empty because the mock provider
        // didn't set them, and ChatClientAgent doesn't generate fallback IDs
        foreach (TextMessageStartEvent startEvent in startEvents)
        {
            Assert.False(
                string.IsNullOrEmpty(startEvent.MessageId),
                "BUG #3433: TextMessageStartEvent.MessageId is null/empty — " +
                "CopilotKit will reject with Zod error: 'Expected string, received null'");
        }

        foreach (TextMessageContentEvent contentEvent in contentEvents)
        {
            Assert.False(
                string.IsNullOrEmpty(contentEvent.MessageId),
                "BUG #3433: TextMessageContentEvent.MessageId is null/empty — " +
                "CopilotKit will reject with Zod error: 'Expected string, received null'");
        }

        // All content events should share the same messageId
        string?[] distinctMessageIds = contentEvents.Select(e => e.MessageId).Distinct().ToArray();
        Assert.Single(distinctMessageIds);
    }

    // =========================================================================
    // Scenario 3: Tool calls with null/empty MessageId (from issue comment)
    // =========================================================================

    /// <summary>
    /// Reproduces the related issue from the comment by @MaciejWarchalowski:
    /// tool call AGUI events have empty parentMessageId, causing downstream issues
    /// with thread persistence and AGUI protocol consistency.
    /// </summary>
    [Fact]
    public async Task Issue3433_ToolCalls_EmptyParentMessageId_ProducesInvalidAGUIEvents()
    {
        // Arrange - Simulate ChatResponseUpdate with a tool call but empty MessageId
        // This is what happens with OpenAI tool calls where MessageId is ""
        FunctionCallContent functionCall = new("call_abc123", "GetWeather")
        {
            Arguments = new Dictionary<string, object?> { ["location"] = "San Francisco" }
        };

        List<ChatResponseUpdate> providerUpdates =
        [
            new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = "", // Empty string — as reported in the issue comment
                Contents = [functionCall]
            }
        ];

        // Act
        List<BaseEvent> aguiEvents = [];
        await foreach (BaseEvent evt in providerUpdates.ToAsyncEnumerableAsync()
            .AsAGUIEventStreamAsync("thread-1", "run-1", AGUIJsonSerializerContext.Default.Options))
        {
            aguiEvents.Add(evt);
        }

        // Assert — ToolCallStartEvent should have a valid parentMessageId
        ToolCallStartEvent? toolCallStart = aguiEvents.OfType<ToolCallStartEvent>().FirstOrDefault();
        Assert.NotNull(toolCallStart);
        Assert.Equal("call_abc123", toolCallStart.ToolCallId);
        Assert.Equal("GetWeather", toolCallStart.ToolCallName);

        // BUG: parentMessageId is empty string "", which breaks AGUI protocol
        Assert.False(
            string.IsNullOrEmpty(toolCallStart.ParentMessageId),
            "BUG #3433: ToolCallStartEvent.ParentMessageId is empty — " +
            "this causes inconsistencies between serialized thread and AGUI events");
    }

    // =========================================================================
    // Scenario 4: AsChatResponseUpdate bypasses AgentResponseUpdate.MessageId
    // =========================================================================

    /// <summary>
    /// Demonstrates the secondary issue: AsChatResponseUpdate() returns the original
    /// ChatResponseUpdate from RawRepresentation, ignoring any MessageId set on the
    /// AgentResponseUpdate wrapper. Even if a pipeline step fixes MessageId on
    /// AgentResponseUpdate, it gets lost when converting back to ChatResponseUpdate.
    /// </summary>
    [Fact]
    public void Issue3433_AsChatResponseUpdate_IgnoresWrapperMessageId()
    {
        // Arrange - Create a ChatResponseUpdate WITHOUT MessageId (from provider)
        ChatResponseUpdate originalUpdate = new(ChatRole.Assistant, "test content");
        // originalUpdate.MessageId is null

        // Wrap it in AgentResponseUpdate (as ChatClientAgent does)
        AgentResponseUpdate agentUpdate = new(originalUpdate)
        {
            AgentId = "test-agent"
        };

        // Simulate a pipeline step setting MessageId on the wrapper
        agentUpdate.MessageId = "fixed-message-id";

        // Act - Convert back to ChatResponseUpdate (as AsChatResponseUpdatesAsync does)
        ChatResponseUpdate result = agentUpdate.AsChatResponseUpdate();

        // Assert
        // BUG: AsChatResponseUpdate returns the original ChatResponseUpdate from
        // RawRepresentation, which still has null MessageId.
        // The "fixed-message-id" set on the AgentResponseUpdate wrapper is lost.
        Assert.Equal(
            "fixed-message-id",
            result.MessageId); // FAILS — returns null because it returns the original
    }

    // =========================================================================
    // Scenario 5: Verify correct behavior (what "fixed" looks like)
    // =========================================================================

    /// <summary>
    /// Control test: demonstrates the expected behavior when MessageId IS set.
    /// This is what happens with providers that properly set MessageId (e.g., OpenAI).
    /// This test should pass — it serves as a reference for what the fix should achieve.
    /// </summary>
    [Fact]
    public async Task Issue3433_Control_WithMessageId_ProducesValidAGUIEvents()
    {
        // Arrange — Provider that properly sets MessageId (like OpenAI)
        List<ChatResponseUpdate> providerUpdates =
        [
            new ChatResponseUpdate(ChatRole.Assistant, "Hello")
            {
                MessageId = "chatcmpl-abc123" // Properly set by provider
            },
            new ChatResponseUpdate(ChatRole.Assistant, " world")
            {
                MessageId = "chatcmpl-abc123" // Same ID for all chunks in one message
            }
        ];

        // Act
        List<BaseEvent> aguiEvents = [];
        await foreach (BaseEvent evt in providerUpdates.ToAsyncEnumerableAsync()
            .AsAGUIEventStreamAsync("thread-1", "run-1", AGUIJsonSerializerContext.Default.Options))
        {
            aguiEvents.Add(evt);
        }

        // Assert — This should pass: messageId is properly set
        List<TextMessageStartEvent> startEvents = aguiEvents.OfType<TextMessageStartEvent>().ToList();
        List<TextMessageContentEvent> contentEvents = aguiEvents.OfType<TextMessageContentEvent>().ToList();

        Assert.Single(startEvents);
        Assert.Equal("chatcmpl-abc123", startEvents[0].MessageId);

        Assert.Equal(2, contentEvents.Count);
        Assert.All(contentEvents, e => Assert.Equal("chatcmpl-abc123", e.MessageId));
    }
}

// =============================================================================
// Mock IChatClient that simulates a provider NOT setting MessageId
// (e.g., Google GenAI / Vertex AI)
// =============================================================================
internal sealed class NullMessageIdChatClient : IChatClient
{
    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Not used in streaming scenario
        return Task.FromResult(new ChatResponse([new(ChatRole.Assistant, "response")]));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Simulate streaming response WITHOUT MessageId — this is the bug trigger
        foreach (string chunk in (string[])["Agents", " are", " autonomous", " programs."])
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent(chunk)]
                // NOTE: MessageId is intentionally NOT set — simulating Google GenAI behavior
            };

            await Task.Yield();
        }
    }
}
