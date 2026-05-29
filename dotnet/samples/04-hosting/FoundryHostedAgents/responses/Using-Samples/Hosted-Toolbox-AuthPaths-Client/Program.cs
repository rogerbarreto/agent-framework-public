// Copyright (c) Microsoft. All rights reserved.

// REPL client for Hosted-Toolbox-AuthPaths sample.
//
// Connects to a locally-running (or remote) Hosted-Toolbox-AuthPaths server, sends user
// prompts to the agent, and prints the assistant text. Each prompt exercises one of the
// toolbox's authentication paths (API key, Entra agent identity, Entra project managed
// identity, inline Authorization).
//
// Required environment variables:
//   AGENT_NAME         - Must match the AGENT_NAME of the running server
//                        (default: hosted-toolbox-auth-paths-agent)
//
// Optional:
//   AGENT_ENDPOINT     - The hosted agent's HTTP endpoint (default: http://localhost:8088)

using System.ClientModel.Primitives;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Extensions.AI;

// Load .env file if present
Env.TraversePath().Load();

Uri agentEndpoint = new(Environment.GetEnvironmentVariable("AGENT_ENDPOINT") ?? "http://localhost:8088");
string agentName = Environment.GetEnvironmentVariable("AGENT_NAME") ?? "hosted-toolbox-auth-paths-agent";

// ── Build the AIProjectClient pointed at the local hosted server ─────────────

var options = new AIProjectClientOptions();

if (agentEndpoint.Scheme == "http")
{
    // For local HTTP dev: tell AIProjectClient the endpoint is HTTPS (to satisfy
    // BearerTokenPolicy's TLS check), then swap the scheme back to HTTP right
    // before the request hits the wire.
    agentEndpoint = new UriBuilder(agentEndpoint) { Scheme = "https" }.Uri;
    options.AddPolicy(new HttpSchemeRewritePolicy(), PipelinePosition.BeforeTransport);
}

var aiProjectClient = new AIProjectClient(agentEndpoint, new AzureCliCredential(), options);
FoundryAgent agent = aiProjectClient.AsAIAgent(new AgentReference(agentName));
AgentSession session = await agent.CreateSessionAsync();

// ── REPL ──────────────────────────────────────────────────────────────────────

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"""
    ══════════════════════════════════════════════════════════
    Hosted-Toolbox-AuthPaths REPL Client
    Connected to: {agentEndpoint}
    Agent:        {agentName}
    Type a message or 'quit' to exit.
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
        AgentResponse response = await agent.RunAsync(input, session);

        // Every toolbox entry in this sample is configured require_approval:never, so no
        // approval requests are expected. Surface a hint if one appears (misconfigured entry).
        if (response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().Any())
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("A tool requested approval; this sample expects require_approval:never on every toolbox entry.");
            Console.ResetColor();
        }

        string text = response.ToString();
        if (!string.IsNullOrWhiteSpace(text))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("Agent> ");
            Console.ResetColor();
            Console.WriteLine(text);
        }
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

// ── HttpSchemeRewritePolicy (for local HTTP dev) ──────────────────────────────

/// <summary>
/// For Local Development Only
/// Rewrites HTTPS URIs to HTTP right before transport, allowing AIProjectClient
/// to target a local HTTP dev server while satisfying BearerTokenPolicy's TLS check.
/// </summary>
internal sealed class HttpSchemeRewritePolicy : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        RewriteScheme(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        RewriteScheme(message);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private static void RewriteScheme(PipelineMessage message)
    {
        var uri = message.Request.Uri!;
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            message.Request.Uri = new UriBuilder(uri) { Scheme = "http" }.Uri;
        }
    }
}
