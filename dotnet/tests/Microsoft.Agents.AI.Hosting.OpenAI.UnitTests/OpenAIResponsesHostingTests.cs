// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.OpenAI.UnitTests;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Tests;

/// <summary>
/// In-process, in-memory hosting tests for <see cref="OpenAIResponses"/>: a request travels through an
/// app-owned ASP.NET Core route (hosted in-memory via <see cref="TestServer"/>) that wires
/// <see cref="OpenAIResponses"/> plus <see cref="HostedAgentState"/> / <see cref="HostedWorkflowState"/>,
/// exactly like the <c>HostingResponsesAgent</c> / <c>HostingResponsesWorkflow</c> samples. These run fully
/// in-process against a deterministic mock chat client — there is no external server process and no live
/// model. Live-model coverage lives in the separate <c>Microsoft.Agents.AI.Hosting.OpenAI.IntegrationTests</c>
/// project. This mirrors the Python <c>test_http_round_trip.py</c> coverage.
/// </summary>
public sealed class OpenAIResponsesHostingTests : IAsyncDisposable
{
    private WebApplication? _app;
    private HttpClient? _client;

    [Fact]
    public async Task AgentRoute_NonStreaming_ReturnsResponsesShapedJsonAsync()
    {
        // Arrange
        HttpClient client = await this.StartAgentHostAsync(new TestHelpers.ConversationMemoryMockChatClient("Hello from the agent"));

        // Act
        HttpResponseMessage response = await client.PostAsync(
            new Uri("/responses", UriKind.Relative),
            JsonContent("""{ "input": "Hi there" }"""));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = doc.RootElement;
        Assert.StartsWith("resp_", root.GetProperty("id").GetString());
        Assert.Equal("response", root.GetProperty("object").GetString());
        Assert.Contains("Hello from the agent", root.GetRawText());
    }

    [Fact]
    public async Task AgentRoute_Streaming_ReturnsServerSentEventsAsync()
    {
        // Arrange
        HttpClient client = await this.StartAgentHostAsync(new TestHelpers.ConversationMemoryMockChatClient("Streamed answer"));

        // Act
        HttpResponseMessage response = await client.PostAsync(
            new Uri("/responses", UriKind.Relative),
            JsonContent("""{ "input": "Hi there", "stream": true }"""));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: response.created", body, StringComparison.Ordinal);
        Assert.Contains("event: response.completed", body, StringComparison.Ordinal);
        Assert.Contains("Streamed answer", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentRoute_MultiTurn_ReusesSessionAcrossTurnsAsync()
    {
        // Arrange: a recording mock so we can prove the second turn saw the first turn's history.
        var recorder = new TestHelpers.ConversationMemoryMockChatClient("ok");
        HttpClient client = await this.StartAgentHostAsync(recorder);

        // Act: first turn, then continue using the returned response id as previous_response_id.
        using JsonDocument first = JsonDocument.Parse(await (await client.PostAsync(
            new Uri("/responses", UriKind.Relative), JsonContent("""{ "input": "first turn" }"""))).Content.ReadAsStringAsync());
        string responseId = first.RootElement.GetProperty("id").GetString()!;

        HttpResponseMessage secondResponse = await client.PostAsync(
            new Uri("/responses", UriKind.Relative),
            JsonContent($$"""{ "input": "second turn", "previous_response_id": "{{responseId}}" }"""));

        // Assert
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(2, recorder.CallHistory.Count);

        // The second call must include the first turn's user message (continuity through HostedAgentState),
        // whereas the first call must not contain the second turn's text.
        string firstCallText = string.Join("\n", recorder.CallHistory[0].Select(m => m.Text));
        string secondCallText = string.Join("\n", recorder.CallHistory[1].Select(m => m.Text));
        Assert.Contains("first turn", secondCallText, StringComparison.Ordinal);
        Assert.DoesNotContain("second turn", firstCallText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentRoute_MalformedBody_ReturnsBadRequestAsync()
    {
        // Arrange
        HttpClient client = await this.StartAgentHostAsync(new TestHelpers.ConversationMemoryMockChatClient("ok"));

        // Act (missing the required "input" field)
        HttpResponseMessage response = await client.PostAsync(
            new Uri("/responses", UriKind.Relative),
            JsonContent("""{ "model": "x" }"""));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WorkflowRoute_RunThenResume_AdvancesCheckpointAcrossTurnsAsync()
    {
        // Arrange: a two-agent sequential workflow behind an app-owned route with checkpoint resume keyed by
        // the stable conversation id.
        HttpClient client = await this.StartWorkflowHostAsync();
        const string ConversationId = "conv_it_1";

        // Act: first turn runs the workflow forward; second turn (same conversation) resumes from the checkpoint.
        HttpResponseMessage firstResponse = await client.PostAsync(
            new Uri("/responses", UriKind.Relative),
            JsonContent($$"""{ "input": "draft this", "conversation": "{{ConversationId}}" }"""));
        HttpResponseMessage secondResponse = await client.PostAsync(
            new Uri("/responses", UriKind.Relative),
            JsonContent($$"""{ "input": "and again", "conversation": "{{ConversationId}}" }"""));

        // Assert: both turns succeed and produce Responses-shaped payloads.
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using JsonDocument doc = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        Assert.StartsWith("resp_", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal(ConversationId, doc.RootElement.GetProperty("conversation").GetProperty("id").GetString());
    }

    private async Task<HttpClient> StartAgentHostAsync(IChatClient chatClient)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        this._app = builder.Build();

        var agent = new ChatClientAgent(chatClient, instructions: "You are a helpful assistant.", name: "assistant");
        var state = new HostedAgentState(agent);

        this._app.MapPost("/responses", async (JsonElement body, HttpContext http, CancellationToken ct) =>
        {
            OpenAIResponsesRunRequest run;
            try
            {
                run = OpenAIResponses.ToAgentRunRequest(body);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest();
            }

            string sessionId = OpenAIResponses.GetSessionId(body) ?? OpenAIResponses.CreateResponseId();
            AgentSession session = await state.GetOrCreateSessionAsync(sessionId, ct);
            string responseId = OpenAIResponses.CreateResponseId();

            if (body.TryGetProperty("stream", out JsonElement s) && s.ValueKind == JsonValueKind.True)
            {
                http.Response.ContentType = "text/event-stream";
                var updates = agent.RunStreamingAsync(run.Messages, session, run.Options, ct);
                await foreach (string frame in OpenAIResponses.WriteResponseStreamAsync(updates, responseId, responseId, ct))
                {
                    await http.Response.WriteAsync(frame, ct);
                }

                await state.SaveSessionAsync(responseId, session, ct);
                return Results.Empty;
            }

            AgentResponse result = await agent.RunAsync(run.Messages, session, run.Options, ct);
            await state.SaveSessionAsync(responseId, session, ct);
            return Results.Json(OpenAIResponses.WriteResponse(result, responseId, responseId));
        });

        await this._app.StartAsync();
        this._client = this.ResolveTestClient();
        return this._client;
    }

    private async Task<HttpClient> StartWorkflowHostAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        this._app = builder.Build();

        AIAgent writer = new ChatClientAgent(new TestHelpers.ConversationMemoryMockChatClient("draft"), name: "Writer");
        AIAgent reviewer = new ChatClientAgent(new TestHelpers.ConversationMemoryMockChatClient("final"), name: "Reviewer");
        Workflow workflow = AgentWorkflowBuilder.BuildSequential(workflowName: "WriteAndReview", agents: [writer, reviewer]);
        var state = new HostedWorkflowState(workflow);

        this._app.MapPost("/responses", async (JsonElement body, CancellationToken ct) =>
        {
            OpenAIResponsesRunRequest run = OpenAIResponses.ToAgentRunRequest(body);
            string sessionId = GetConversationId(body) ?? OpenAIResponses.CreateResponseId();

            HostedWorkflowRunResult result = await state.RunOrResumeAsync(sessionId, run.Messages.ToList(), ct);

            var response = new AgentResponse(new ChatMessage(ChatRole.Assistant, $"{result.Events.Count} event(s)"));
            return Results.Json(OpenAIResponses.WriteResponse(response, OpenAIResponses.CreateResponseId(), sessionId));
        });

        await this._app.StartAsync();
        this._client = this.ResolveTestClient();
        return this._client;
    }

    private HttpClient ResolveTestClient()
    {
        TestServer server = this._app!.Services.GetRequiredService<IServer>() as TestServer
            ?? throw new InvalidOperationException("TestServer not found");
        return server.CreateClient();
    }

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    private static string? GetConversationId(JsonElement body)
    {
        if (!body.TryGetProperty("conversation", out JsonElement conversation))
        {
            return null;
        }

        return conversation.ValueKind switch
        {
            JsonValueKind.String => conversation.GetString(),
            JsonValueKind.Object when conversation.TryGetProperty("id", out JsonElement id) => id.GetString(),
            _ => null,
        };
    }

    public async ValueTask DisposeAsync()
    {
        this._client?.Dispose();
        if (this._app is not null)
        {
            await this._app.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
