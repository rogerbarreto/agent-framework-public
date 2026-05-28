// Copyright (c) Microsoft. All rights reserved.

// Mounts an AIAgent on the JSON Invocations channel and exposes:
//   POST /invocations/invoke              run the agent synchronously
//   POST /invocations/invoke with background:true  return a continuation token
//   GET  /invocations/{continuationToken} poll for a queued / completed run

#pragma warning disable CA1031 // demo-only top-level exception handling

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.Channels;
using Microsoft.Agents.AI.Hosting.Channels.Invocations;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using OpenAI.Chat;

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";

// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
// latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
    .GetChatClient(deploymentName)
    .AsAIAgent(name: "Concierge", instructions: "You are a helpful concierge.");

var builder = WebApplication.CreateBuilder(args);

// <add_host>
builder.AddAgentFrameworkHost(agent)
    .AddInvocationsChannel();
// </add_host>

var app = builder.Build();
app.MapAgentFrameworkHost();
app.Run();