// Copyright (c) Microsoft. All rights reserved.

// This sample shows how an application can own its own ASP.NET Core route and expose an AIAgent over the
// OpenAI Responses protocol by calling the Agent Framework OpenAIResponses conversion helpers, instead of
// using the batteries-included MapOpenAIResponses server. The application keeps control of routing, auth,
// and session storage; the helpers provide only the protocol <-> agent conversion.

using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using OpenAI.Chat;

var builder = WebApplication.CreateBuilder(args);

// Configuration via environment variables (never hardcode secrets).
string endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
string deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o-mini";

AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
    .GetChatClient(deployment)
    .AsAIAgent(instructions: "You are a helpful assistant.", name: "Assistant");

// Optional shared execution state: pairs the agent with a session store (in-memory by default).
var state = new HostedAgentState(agent);

var app = builder.Build();

// The application owns this route. It parses the OpenAI Responses body with the helpers, runs the agent
// itself, and renders the response with the helpers.
app.MapPost("/responses", async (HttpContext http, CancellationToken cancellationToken) =>
{
    using JsonDocument doc = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
    JsonElement body = doc.RootElement;

    // The candidate continuation id is untrusted. A real app authenticates the caller and authorizes/binds
    // this key to the principal before using it. This sample simply falls back to a fresh id.
    string? candidateSessionId = OpenAIResponses.GetSessionId(body);
    string sessionId = Authorize(http, candidateSessionId) ?? OpenAIResponses.CreateResponseId();

    OpenAIResponsesRunRequest run = OpenAIResponses.ToAgentRunRequest(body);
    AgentSession session = await state.GetOrCreateSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
    string responseId = OpenAIResponses.CreateResponseId();

    bool stream = body.TryGetProperty("stream", out JsonElement streamProp) && streamProp.ValueKind == JsonValueKind.True;

    if (stream)
    {
        http.Response.ContentType = "text/event-stream";
        var updates = agent.RunStreamingAsync(run.Messages, session, run.Options, cancellationToken);
        await foreach (string frame in OpenAIResponses.WriteResponseStreamAsync(updates, responseId, responseId, cancellationToken).ConfigureAwait(false))
        {
            await http.Response.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await http.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        // Persist the post-run session under the new response id so the next turn can continue from it.
        await state.SaveSessionAsync(responseId, session, cancellationToken).ConfigureAwait(false);
        return Results.Empty;
    }

    AgentResponse result = await agent.RunAsync(run.Messages, session, run.Options, cancellationToken).ConfigureAwait(false);
    await state.SaveSessionAsync(responseId, session, cancellationToken).ConfigureAwait(false);
    return Results.Json(OpenAIResponses.WriteResponse(result, responseId, responseId));
});

app.Run();

// Application-owned trust decision. Replace with real authentication + authorization: verify the caller,
// then authorize/bind the candidate id to the authenticated principal before returning it.
static string? Authorize(HttpContext http, string? candidateSessionId) => candidateSessionId;
