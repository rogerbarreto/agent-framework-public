// Copyright (c) Microsoft. All rights reserved.

// This sample shows how an application can own its own ASP.NET Core route and expose a workflow over the
// OpenAI Responses protocol. It uses the OpenAIResponses conversion helpers for the wire protocol and
// HostedWorkflowState for per-session checkpoint resume. The application keeps control of routing, auth,
// and checkpoint storage.

using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Agents.AI.Workflows;
using OpenAI.Chat;

var builder = WebApplication.CreateBuilder(args);

// Configuration via environment variables (never hardcode secrets).
string endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
string deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o-mini";

var chatClient = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential()).GetChatClient(deployment);
AIAgent writer = chatClient.AsAIAgent(instructions: "Write a concise first draft for the user's request.", name: "Writer");
AIAgent reviewer = chatClient.AsAIAgent(instructions: "Improve the draft and produce the final answer.", name: "Reviewer");

Workflow workflow = AgentWorkflowBuilder.BuildSequential(workflowName: "WriteAndReview", agents: [writer, reviewer]);

// Optional shared execution state: pairs the workflow with an in-memory CheckpointManager and a per-session
// sessionId -> CheckpointInfo head cursor so a session can resume from its last checkpoint.
var state = new HostedWorkflowState(workflow);

var app = builder.Build();

// The application owns this route.
app.MapPost("/responses", async (HttpContext http, CancellationToken cancellationToken) =>
{
    using JsonDocument doc = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
    JsonElement body = doc.RootElement;

    // The candidate continuation id is untrusted. A real app authenticates the caller and authorizes/binds
    // this key before using it as the workflow's checkpoint session id. This sample falls back to a fresh id.
    string sessionId = OpenAIResponses.GetSessionId(body) ?? OpenAIResponses.CreateResponseId();

    OpenAIResponsesRunRequest run = OpenAIResponses.ToAgentRunRequest(body);

    // Runs the workflow forward on the first call for this session, or resumes from the session's last
    // checkpoint thereafter, then records the new head checkpoint for the session.
    HostedWorkflowRunResult result = await state.RunOrResumeAsync(sessionId, run.Messages.ToList(), cancellationToken).ConfigureAwait(false);

    // Real applications extract the workflow's output from the emitted events per their workflow's design.
    // For illustration this sample summarizes the run and echoes the recorded checkpoint id.
    var summary = new StringBuilder()
        .Append(result.Events.Count)
        .Append(" workflow event(s) processed.");
    if (result.Checkpoint is not null)
    {
        summary.Append(" Checkpoint: ").Append(result.Checkpoint.CheckpointId).Append('.');
    }

    var response = new AgentResponse(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, summary.ToString()));
    return Results.Json(OpenAIResponses.WriteResponse(response, OpenAIResponses.CreateResponseId(), sessionId));
});

app.Run();
