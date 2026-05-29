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
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Moq;

namespace Microsoft.Agents.AI.Foundry.Hosting.UnitTests;

/// <summary>
/// Coverage for the OAuth-consent emission path on <see cref="AgentFrameworkResponseHandler"/>.
/// Mirrors the Python reference contract in <c>agent_framework_foundry_hosting/_responses.py</c>
/// (<c>CONSENT_ERROR_CODE = -32007</c>) and the associated test
/// <c>tests/test_responses.py::TestOAuthConsentSurfacing</c>.
/// </summary>
public class AgentFrameworkResponseHandlerOAuthConsentTests
{
    private const string ConsentLink = "https://login.example.com/oauth/authorize?client_id=abc&state=xyz";
    private const string ToolboxName = "auth-paths-oauth-toolbox";
    private const string ToolName = "github_oauth___get_me";

    [Fact]
    public async Task CreateAsync_ConsentSignalledByConnect_EmitsOAuthConsentRequestItemAsync()
    {
        // Arrange: an agent that simulates the consent-aware MCP connect path - it records
        // a pending consent on the ambient RequestConsentState, cancels its linked CTS,
        // and surfaces the resulting OperationCanceledException to the handler.
        var agent = new ConsentSignallingAgent(toolName: string.Empty, simulateAtConnect: true);
        var events = await RunHandlerAsync(agent).ConfigureAwait(true);

        // Assert: stream contains an oauth_consent_request output item then incomplete.
        var added = Assert.Single(events.OfType<ResponseOutputItemAddedEvent>());
        var item = Assert.IsType<OAuthConsentRequestOutputItem>(added.Item);
        Assert.Equal(ConsentLink, item.ConsentLink);
        Assert.Equal(ToolboxName, item.ServerLabel);
        Assert.StartsWith("oacr_", item.Id, StringComparison.Ordinal);

        var done = Assert.Single(events.OfType<ResponseOutputItemDoneEvent>());
        Assert.Same(item, Assert.IsType<OAuthConsentRequestOutputItem>(done.Item));

        Assert.Single(events.OfType<ResponseIncompleteEvent>());
        Assert.Empty(events.OfType<ResponseCompletedEvent>());
        Assert.Empty(events.OfType<ResponseFailedEvent>());
    }

    [Fact]
    public async Task CreateAsync_ConsentSignalledByToolCall_EmitsOAuthConsentRequestItemAsync()
    {
        // Arrange: same surface but the consent comes from a per-tool invocation (e.g. consent
        // revoked mid-session). Handler behaviour should be identical.
        var agent = new ConsentSignallingAgent(toolName: ToolName, simulateAtConnect: false);
        var events = await RunHandlerAsync(agent).ConfigureAwait(true);

        // Assert
        var added = Assert.Single(events.OfType<ResponseOutputItemAddedEvent>());
        var item = Assert.IsType<OAuthConsentRequestOutputItem>(added.Item);
        Assert.Equal(ConsentLink, item.ConsentLink);
        Assert.Equal(ToolboxName, item.ServerLabel);

        Assert.Single(events.OfType<ResponseOutputItemDoneEvent>());
        Assert.Single(events.OfType<ResponseIncompleteEvent>());
    }

    [Fact]
    public async Task CreateAsync_ConsentEmission_PrecedesIncompleteEventAsync()
    {
        // Arrange
        var agent = new ConsentSignallingAgent(toolName: ToolName, simulateAtConnect: false);
        var events = await RunHandlerAsync(agent).ConfigureAwait(true);

        // Assert: ordering matches Python contract - added, done, incomplete (in that order),
        // with no spurious events afterwards.
        var addedIndex = events.FindIndex(e => e is ResponseOutputItemAddedEvent);
        var doneIndex = events.FindIndex(e => e is ResponseOutputItemDoneEvent);
        var incompleteIndex = events.FindIndex(e => e is ResponseIncompleteEvent);

        Assert.True(addedIndex >= 0, "Expected ResponseOutputItemAddedEvent in stream.");
        Assert.True(addedIndex < doneIndex, "Added must precede Done.");
        Assert.True(doneIndex < incompleteIndex, "Done must precede Incomplete.");
        Assert.Equal(incompleteIndex, events.Count - 1);
    }

    [Fact]
    public void FoundryConsentErrorHelper_DetectsConsentRequiredErrorCode()
    {
        // Arrange
        var ex = new McpProtocolException(ConsentLink, (McpErrorCode)(-32007));

        // Act
        var detected = FoundryConsentErrorHelper.TryGetConsentLink(ex, out var link);

        // Assert
        Assert.True(detected);
        Assert.Equal(ConsentLink, link);
    }

    [Fact]
    public void FoundryConsentErrorHelper_IgnoresUnrelatedErrorCodes()
    {
        // Arrange
        var ex = new McpProtocolException("some other failure", McpErrorCode.InvalidParams);

        // Act
        var detected = FoundryConsentErrorHelper.TryGetConsentLink(ex, out var link);

        // Assert
        Assert.False(detected);
        Assert.Null(link);
    }

    [Fact]
    public void FoundryConsentErrorHelper_UnwrapsInnerExceptions()
    {
        // Arrange: consent error wrapped (mirrors Python ToolExecutionException pattern).
        var inner = new McpProtocolException(ConsentLink, (McpErrorCode)(-32007));
        var ex = new InvalidOperationException("wrapper", inner);

        // Act
        var detected = FoundryConsentErrorHelper.TryGetConsentLink(ex, out var link);

        // Assert
        Assert.True(detected);
        Assert.Equal(ConsentLink, link);
    }

    private static async Task<List<ResponseStreamEvent>> RunHandlerAsync(AIAgent agent)
    {
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton(agent);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test-model" };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new
            {
                type = "message",
                id = "msg_1",
                status = "completed",
                role = "user",
                content = new[] { new { type = "input_text", text = "call the github oauth tool" } }
            }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
        {
            events.Add(evt);
        }

        return events;
    }

    /// <summary>
    /// Simulates the consent-aware MCP layer: records a pending consent on the ambient
    /// <see cref="McpConsentContext.Current"/> and cancels its linked CTS, then surfaces an
    /// <see cref="OperationCanceledException"/> as the consent-aware components do via
    /// <c>cancellationToken.ThrowIfCancellationRequested()</c>.
    /// </summary>
    private sealed class ConsentSignallingAgent(string toolName, bool simulateAtConnect) : AIAgent
    {
        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Mimic the consent-aware components: read the ambient consent state, populate
            // the pending consent info and cancel the linked CTS, then propagate OCE.
            var ok = FoundryConsentErrorHelper.TryRecord(
                ToolboxName,
                toolName,
                ConsentLink);

            Assert.True(ok, "Expected RequestConsentState to be active during agent run.");

            // Yield a no-op so the enumerator advances at least once before the OCE fires;
            // the simulateAtConnect path skips even this to mirror the connect-time surface.
            if (!simulateAtConnect)
            {
                yield return new AgentResponseUpdate { Contents = [] };
                await Task.Yield();
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Unreachable in practice but required by the async iterator contract.
            yield break;
        }

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(
            CancellationToken cancellationToken = default) =>
            new(new SimpleSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default) =>
            new(JsonDocument.Parse("{}").RootElement);

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default) =>
            new(new SimpleSession());

        private sealed class SimpleSession : AgentSession;
    }
}
