// Copyright (c) Microsoft. All rights reserved.

using System.ClientModel;
using Azure.AI.Projects;
using Azure.Identity;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using OpenAI;
using OpenAI.Responses;

// Load .env file if present (for local development)
Env.TraversePath().Load();

// Port the Hosted-* samples listen on when run locally with `dotnet run`.
const int LocalAgentPort = 8088;

// AZURE_AI_AGENT_NAME is the registered server-side agent name.
string agentName = Environment.GetEnvironmentVariable("AZURE_AI_AGENT_NAME")
    ?? throw new InvalidOperationException("AZURE_AI_AGENT_NAME is not set.");

// Ask which server to talk to. This is the same choice `azd ai agent invoke` exposes through its
// --local flag, asked at startup instead of passed as an argument.
bool useLocalAgent = PromptForLocalTarget();

AIAgent agent = useLocalAgent ? CreateLocalAgent() : CreateHostedAgent(agentName);
string target = useLocalAgent ? $"http://localhost:{LocalAgentPort}" : agentName;

AgentSession session = await agent.CreateSessionAsync();

// ── REPL ──────────────────────────────────────────────────────────────────────

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"""
    ══════════════════════════════════════════════════════════
    Simple Agent Sample
    Connected to: {target}
    Type a message or 'quit' to exit
    ══════════════════════════════════════════════════════════
    """);
Console.ResetColor();
Console.WriteLine();

while (true)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("You> ");
    Console.ResetColor();

    string? input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input)) { continue; }
    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) { break; }

    try
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("Agent> ");
        Console.ResetColor();

        await foreach (var update in agent.RunStreamingAsync(input, session))
        {
            Console.Write(update);
        }

        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {ex.Message}");
        Console.ResetColor();
    }

    Console.WriteLine();
}

Console.WriteLine("Goodbye!");

// Asks whether to target a locally running agent or the one deployed to Foundry, and returns
// true for local. Defaults to remote on an empty answer, matching `azd ai agent invoke`, which
// targets Foundry unless --local is passed.
static bool PromptForLocalTarget()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("Which agent do you want to chat with?");
    Console.ResetColor();
    Console.WriteLine("  [1] Foundry (deployed agent)   [default]");
    Console.WriteLine($"  [2] Local    (dotnet run, http://localhost:{LocalAgentPort})");
    Console.Write("Choice: ");

    string? choice = Console.ReadLine()?.Trim();
    Console.WriteLine();

    return choice is "2";
}

// Builds an agent against a Hosted-* sample running locally. The sample serves the standard
// Responses route (POST /responses), so an OpenAI responses client pointed at the server reaches
// it directly. The server hosts its own agent and ignores both the model id and the api key, but
// the SDK requires them to shape the request.
static AIAgent CreateLocalAgent()
{
    var options = new OpenAIClientOptions { Endpoint = new Uri($"http://localhost:{LocalAgentPort}") };

    return new OpenAIClient(new ApiKeyCredential("not-needed"), options)
        .GetResponsesClient()
        .AsAIAgent(model: "hosted-agent", name: "LocalHostedAgent");
}

// Builds an agent against an agent deployed to Foundry. Hosted agents are reached through their
// per-agent endpoint, which the platform routes to the container's /responses route.
static AIAgent CreateHostedAgent(string agentName)
{
    Uri projectEndpoint = new(Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
        ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set."));

    Uri agentEndpoint = new($"{projectEndpoint}/agents/{agentName}/endpoint/protocols/openai");

    return new AIProjectClient(projectEndpoint, new AzureCliCredential()).AsAIAgent(agentEndpoint);
}
