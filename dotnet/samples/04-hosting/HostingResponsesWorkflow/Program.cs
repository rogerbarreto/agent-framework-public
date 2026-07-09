// Copyright (c) Microsoft. All rights reserved.

// This sample shows how an application can own its own ASP.NET Core route and expose a workflow over the
// OpenAI Responses protocol. It uses the OpenAIResponses conversion helpers for the wire protocol and
// HostedWorkflowState for per-session checkpoint resume. The application keeps control of routing, auth,
// and checkpoint storage.

using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// Configuration via environment variables (never hardcode secrets).
string endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set.");
string model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL") ?? "gpt-5.4-mini";

// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
// latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
var projectClient = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
AIAgent writer = projectClient.AsAIAgent(model: model, instructions: "Write a concise first draft for the user's request.", name: "Writer");
AIAgent reviewer = projectClient.AsAIAgent(model: model, instructions: "Improve the draft and produce the final answer.", name: "Reviewer");

Workflow workflow = AgentWorkflowBuilder.BuildSequential(workflowName: "WriteAndReview", agents: [writer, reviewer]);

// The last agent in the sequential pipeline produces the workflow's final answer. The streaming path filters
// updates to this agent so the streamed response matches the non-streaming final-message response.
string finalAgentName = reviewer.Name ?? "Reviewer";

// Optional shared execution state: pairs the workflow with an in-memory CheckpointManager and a per-session
// sessionId -> CheckpointInfo head cursor so a session can resume from its last checkpoint.
var state = new HostedWorkflowState(workflow);

var app = builder.Build();

// The application owns this route.
app.MapPost("/responses", async (HttpContext http, CancellationToken cancellationToken) =>
{
    using JsonDocument doc = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
    JsonElement body = doc.RootElement;

    // Workflow checkpoint resume needs a STABLE key across turns. previous_response_id changes every turn,
    // so key on the conversation id (constant for the conversation). The candidate is untrusted: a real app
    // authenticates the caller and authorizes/binds this key before using it. This sample falls back to a fresh id.
    string sessionId = GetConversationId(body) ?? OpenAIResponses.CreateResponseId();

    OpenAIResponsesRunRequest run = OpenAIResponses.ToAgentRunRequest(body);
    var messages = run.Messages.ToList();

    bool stream = body.TryGetProperty("stream", out JsonElement streamProp) && streamProp.ValueKind == JsonValueKind.True;

    if (stream)
    {
        // Stream the workflow's agent updates back over the Responses SSE wire. Runs forward on the first
        // call for this session, or restores the session's latest checkpoint and runs forward with this
        // turn's input thereafter, recording the new head checkpoint once the stream completes.
        http.Response.ContentType = "text/event-stream";
        string streamResponseId = OpenAIResponses.CreateResponseId();
        IAsyncEnumerable<AgentResponseUpdate> updates = ExtractUpdates(state.RunOrResumeStreamingAsync(sessionId, messages, cancellationToken), finalAgentName, cancellationToken);

        await foreach (string frame in OpenAIResponses.WriteResponseStreamAsync(updates, streamResponseId, sessionId, cancellationToken).ConfigureAwait(false))
        {
            await http.Response.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await http.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return Results.Empty;
    }

    // Runs the workflow forward on the first call for this session, or restores the session's latest
    // checkpoint and runs forward with this turn's input thereafter, then records the new head checkpoint.
    HostedWorkflowRunResult result = await state.RunOrResumeAsync(sessionId, messages, cancellationToken).ConfigureAwait(false);

    // Render the workflow's output. Applications extract output from the emitted events per their
    // workflow's design; here we surface the final assistant message the pipeline produced this turn.
    AgentResponse response = BuildWorkflowResponse(result);
    return Results.Json(OpenAIResponses.WriteResponse(response, OpenAIResponses.CreateResponseId(), sessionId));
});

app.Run();

// Projects the final agent's streaming updates from the emitted workflow events, so the streamed response
// matches the non-streaming final-message response rather than also streaming intermediate agents' drafts.
static async IAsyncEnumerable<AgentResponseUpdate> ExtractUpdates(
    IAsyncEnumerable<WorkflowEvent> events,
    string finalAgentName,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    await foreach (WorkflowEvent evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
    {
        if (evt is AgentResponseUpdateEvent updateEvent &&
            string.Equals(updateEvent.Update.AuthorName, finalAgentName, StringComparison.Ordinal))
        {
            yield return updateEvent.Update;
        }
    }
}

// Extracts the final assistant message produced by the workflow this turn from its output events,
// falling back to a short run summary when the workflow emitted no message output.
static AgentResponse BuildWorkflowResponse(HostedWorkflowRunResult result)
{
    ChatMessage? finalMessage = null;
    foreach (WorkflowEvent evt in result.Events)
    {
        if (evt is WorkflowOutputEvent output && output.Data is IEnumerable<ChatMessage> messages)
        {
            foreach (ChatMessage message in messages)
            {
                if (message.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(message.Text))
                {
                    finalMessage = message;
                }
            }
        }
    }

    return finalMessage is not null
        ? new AgentResponse(finalMessage)
        : new AgentResponse(new ChatMessage(ChatRole.Assistant, $"{result.Events.Count} workflow event(s) processed."));
}

// Reads the stable conversation id (string or object form) from the request body. Unlike previous_response_id,
// the conversation id is constant across turns, so it is a valid workflow checkpoint session key.
static string? GetConversationId(JsonElement body)
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
