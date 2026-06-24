// Copyright (c) Microsoft. All rights reserved.

// Exposes an AIAgent on the OpenAI Responses-shaped channel.
//   POST /responses  { "input": "Hi" }                 -> Responses JSON
//   POST /responses  { "input": "Hi", "stream": true } -> SSE stream
// Session continuity is keyed by ChannelSession.IsolationKey; identical keys resolve to the same
// cached AgentSession. This sample lets the channel derive the session from previous_response_id.

#pragma warning disable CA1031

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.Channels.Responses;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using OpenAI.Chat;

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";

// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
// latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
    .GetChatClient(deployment)
    .AsAIAgent(name: "Concierge", instructions: "You are a helpful concierge.");

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentFrameworkHost(agent)
    .AddResponsesChannel();

var app = builder.Build();
app.MapAgentFrameworkHost();
app.Run();