// Copyright (c) Microsoft. All rights reserved.

// Hosted Foundry Agent - wraps an existing Foundry-managed (prompt) agent definition and serves it
// over the Responses protocol as a hosted agent. The managed agent is retrieved by name. It is
// deployed to Foundry directly from source (code / ZIP upload), so the platform builds and runs your
// code with no container image.

using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using DotNetEnv;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Agents.AI.Foundry.Hosting;

// Load a local .env file when present (local development only). In Foundry the
// platform injects the required environment variables at runtime.
Env.TraversePath().Load();

var projectEndpoint = new Uri(System.Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set."));

// The Foundry-managed agent to wrap, by name. When hosted, the platform injects AGENT_NAME as this
// hosted agent's own name, so the hosted agent and the managed agent it wraps share a name.
var agentName = System.Environment.GetEnvironmentVariable("AGENT_NAME")
    ?? throw new InvalidOperationException("AGENT_NAME is not set.");

// WARNING: DefaultAzureCredential is convenient for development but requires careful
// consideration in production. Consider a specific credential (for example
// ManagedIdentityCredential) to avoid latency, unintended credential probing, and
// fallback security risks.
var aiProjectClient = new AIProjectClient(projectEndpoint, new DefaultAzureCredential());

// Retrieve the Foundry-managed agent by name (latest version).
ProjectsAgentRecord agentRecord = await aiProjectClient
    .AgentAdministrationClient.GetAgentAsync(agentName);

FoundryAgent agent = aiProjectClient.AsAIAgent(agentRecord);

// Host the agent using the Responses protocol.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFoundryResponses(agent);

var app = builder.Build();
app.MapFoundryResponses();

app.Run();
