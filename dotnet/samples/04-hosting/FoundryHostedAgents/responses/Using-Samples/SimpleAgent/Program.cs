// Copyright (c) Microsoft. All rights reserved.

using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

// Load .env file if present (for local development)
Env.TraversePath().Load();

// FOUNDRY_PROJECT_ENDPOINT is the Foundry project endpoint. Shape:
//   https://<host>/api/projects/<project>
Uri projectEndpoint = new(Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set."));

// AZURE_AI_AGENT_NAME is the registered server-side agent name.
string agentName = Environment.GetEnvironmentVariable("AZURE_AI_AGENT_NAME")
    ?? throw new InvalidOperationException("AZURE_AI_AGENT_NAME is not set.");

// Derive the per-agent OpenAI endpoint that hosted Foundry agents require.
Uri agentEndpoint = new($"{projectEndpoint}/agents/{agentName}/endpoint/protocols/openai");

// ── Create an agent-framework agent backed by the remote agent endpoint ──────

// Per-agent client options. The scheme-rewrite policy for local HTTP dev must live on
// THIS options bag (the per-agent responses pipeline), not on AIProjectClientOptions:
// FoundryAgent builds the responses client from these options, so a policy added here
// is the one that actually runs on the /responses request.
var clientOptions = new ProjectOpenAIClientOptions();

if (agentEndpoint.Scheme == "http")
{
    // For local HTTP dev: present the endpoint as HTTPS (to satisfy BearerTokenPolicy's
    // TLS check), then swap the scheme back to HTTP right before the request hits the
    // wire. Rewriting the agent endpoint preserves its explicit port (for example 8088).
    agentEndpoint = new UriBuilder(agentEndpoint) { Scheme = "https" }.Uri;
    clientOptions.AddPolicy(new HttpSchemeRewritePolicy(), PipelinePosition.BeforeTransport);
}

// FoundryAgent's agent-endpoint constructor builds the responses client directly from
// agentEndpoint (port preserved) and applies clientOptions to that pipeline. It expects a
// System.ClientModel AuthenticationTokenProvider, so adapt the Azure CLI credential.
var credential = new AzureCliAuthenticationTokenProvider(new AzureCliCredential(), "https://ai.azure.com/.default");
FoundryAgent agent = new(agentEndpoint, credential, clientOptions);

AgentSession session = await agent.CreateSessionAsync();

// ── REPL ──────────────────────────────────────────────────────────────────────

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"""
    ══════════════════════════════════════════════════════════
    Simple Agent Sample
    Connected to: {agentEndpoint}
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

/// <summary>
/// For Local Development Only
/// Adapts an Azure.Core <see cref="TokenCredential"/> (for example <see cref="AzureCliCredential"/>)
/// to the System.ClientModel <see cref="AuthenticationTokenProvider"/> that the
/// <see cref="FoundryAgent"/> agent-endpoint constructor expects. Uses the fixed Azure AI resource
/// scope that the Foundry control plane accepts.
/// </summary>
internal sealed class AzureCliAuthenticationTokenProvider(TokenCredential credential, params string[] scopes)
    : AuthenticationTokenProvider
{
    public override GetTokenOptions? CreateTokenOptions(IReadOnlyDictionary<string, object> properties)
        => new(properties);

    public override AuthenticationToken GetToken(GetTokenOptions options, CancellationToken cancellationToken)
    {
        AccessToken token = credential.GetToken(new TokenRequestContext(scopes), cancellationToken);
        return new AuthenticationToken(token.Token, "Bearer", token.ExpiresOn);
    }

    public override async ValueTask<AuthenticationToken> GetTokenAsync(GetTokenOptions options, CancellationToken cancellationToken)
    {
        AccessToken token = await credential.GetTokenAsync(new TokenRequestContext(scopes), cancellationToken).ConfigureAwait(false);
        return new AuthenticationToken(token.Token, "Bearer", token.ExpiresOn);
    }
}
