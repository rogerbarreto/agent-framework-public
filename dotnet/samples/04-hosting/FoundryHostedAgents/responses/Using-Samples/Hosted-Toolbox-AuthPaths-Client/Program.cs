// Copyright (c) Microsoft. All rights reserved.

// REPL client for Hosted-Toolbox-AuthPaths sample.
//
// Connects to a locally-running (or remote) Hosted-Toolbox-AuthPaths server, sends user
// prompts to the agent, and handles BOTH:
//   1. Standard text streaming responses (printed inline).
//   2. OAuth `mcp_approval_request` items emitted when a toolbox MCP tool requires user
//      consent (custom OAuth path #4 in the sample). The REPL detects ToolApprovalRequestContent
//      in the response, prints the consent URL, opens it in the default browser, and waits
//      for the user to press Enter before resubmitting an approval response on the same session.
//
// Required environment variables:
//   AGENT_NAME         - Must match the AGENT_NAME of the running server
//                        (default: hosted-toolbox-auth-paths-agent)
//
// Optional:
//   AGENT_ENDPOINT     - The hosted agent's HTTP endpoint (default: http://localhost:8088)

using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    OAuth consent prompts will open in your browser as needed.
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
        await HandleResponseAsync(agent, session, response);
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

// ── Approval-aware response handler ───────────────────────────────────────────

static async Task HandleResponseAsync(FoundryAgent agent, AgentSession session, AgentResponse response)
{
    // Loop while the response carries any approval requests we can satisfy.
    while (true)
    {
        List<ToolApprovalRequestContent> approvalRequests = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<ToolApprovalRequestContent>()
            .ToList();

        if (approvalRequests.Count == 0)
        {
            // No approvals pending — print the assistant text and return.
            string text = response.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("Agent> ");
                Console.ResetColor();
                Console.WriteLine(text);
            }
            return;
        }

        // Build approval responses; for OAuth-style approvals we also surface the consent
        // URL (when present) so the user can complete consent in a browser before approving.
        var approvalMessages = new List<ChatMessage>();

        foreach (var request in approvalRequests)
        {
            string toolName = request.ToolCall is FunctionCallContent fc ? fc.Name : "<unknown>";
            string? consentUrl = TryExtractConsentUrl(request);

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"\nApproval requested for tool: {toolName}");
            if (consentUrl is not null)
            {
                Console.WriteLine($"OAuth consent URL: {consentUrl}");
                TryOpenBrowser(consentUrl);
                Console.WriteLine("Complete consent in the browser, then press Enter to continue (or 'n' to reject).");
            }
            else
            {
                Console.WriteLine("Press Enter to approve (or 'n' to reject).");
            }
            Console.ResetColor();

            string? answer = Console.ReadLine();
            bool approved = !string.Equals(answer?.Trim(), "n", StringComparison.OrdinalIgnoreCase);

            approvalMessages.Add(new ChatMessage(ChatRole.User, [request.CreateResponse(approved)]));
        }

        response = await agent.RunAsync(approvalMessages, session);
    }
}

// ── Consent-URL extractor ─────────────────────────────────────────────────────
//
// The AF Foundry hosting bridge emits the OAuth consent URL on the wire as part of an
// `mcp_approval_request` item. The deserialized ToolApprovalRequestContent surfaces it via
// the wrapped FunctionCallContent's Arguments dictionary OR (depending on SDK version) as a
// raw URL string somewhere in the content. This helper scans the request's content shape
// pragmatically and returns the first URL it finds.

static string? TryExtractConsentUrl(ToolApprovalRequestContent request)
{
    if (request.ToolCall is not FunctionCallContent fc) { return null; }

    if (fc.Arguments is { Count: > 0 } args)
    {
        foreach (var value in args.Values)
        {
            if (value is null) { continue; }
            string serialized = value as string ?? JsonSerializer.Serialize(value);
            var match = ConsentUrlRegex().Match(serialized);
            if (match.Success) { return match.Value; }
        }
    }

    return null;
}

static void TryOpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"(Could not open browser automatically: {ex.Message}. Open the URL manually.)");
        Console.ResetColor();
    }
}

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

internal static partial class Program
{
    [GeneratedRegex(@"https?://[^\s""'\\]+", RegexOptions.None)]
    public static partial Regex ConsentUrlRegex();
}
