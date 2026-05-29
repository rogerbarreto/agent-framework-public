// Copyright (c) Microsoft. All rights reserved.

// REPL client for the Hosted-Toolbox-AuthPaths sample.
//
// Connects to a Foundry-hosted agent (server-side toolbox with four auth paths) and
// streams responses over the OpenAI Responses SSE protocol. The educational surface in
// this sample is how the client handles two distinct interactive flows:
//
//   1. Standard text streaming responses (printed inline as `response.output_text.delta`).
//   2. OAuth `oauth_consent_request` items emitted when a toolbox MCP tool requires user
//      consent (path #4). On detection the REPL prints the consent URL, opens the system
//      browser, and waits for the user to press Enter before re-issuing the same prompt
//      with `previous_response_id` so the model can continue once the consent is granted.
//
// The client uses raw HttpClient + SSE parsing rather than an SDK because the .NET OpenAI
// 2.9.1 client does not know the `oauth_consent_request` item type and surfaces it as an
// internal unknown item that callers cannot inspect. Going to the wire keeps the sample
// faithful to the Python reference and resilient to future SDK additions.
//
// Required environment variables:
//   AZURE_AI_PROJECT_ENDPOINT  - Foundry project endpoint
//                                (e.g. https://<host>/api/projects/<project>)
//   AZURE_AI_AGENT_NAME        - Registered server-side agent name
//                                (default: hosted-toolbox-auth-paths-agent)
// Optional:
//   AZURE_AI_MODEL             - Model name advertised in the request body
//                                (default: gpt-4o; the hosted agent ignores this and uses
//                                its own server-side deployment, so any non-empty value works)

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using DotNetEnv;

Env.TraversePath().Load();

string projectEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is not set.");
string agentName = Environment.GetEnvironmentVariable("AZURE_AI_AGENT_NAME")
    ?? "hosted-toolbox-auth-paths-agent";
string model = Environment.GetEnvironmentVariable("AZURE_AI_MODEL") ?? "gpt-4o";

Uri responsesEndpoint = new($"{projectEndpoint.TrimEnd('/')}/agents/{agentName}/endpoint/protocols/openai/responses?api-version=v1");

TokenCredential credential = new DefaultAzureCredential();
var tokenContext = new TokenRequestContext(["https://ai.azure.com/.default"]);

using var http = new HttpClient
{
    Timeout = Timeout.InfiniteTimeSpan,
};

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"""
    ══════════════════════════════════════════════════════════
    Hosted-Toolbox-AuthPaths REPL Client
    Endpoint : {responsesEndpoint}
    Agent    : {agentName}
    Type a message, '/new' to drop conversation state, or 'quit'.
    OAuth consent prompts open in your browser as needed.
    ══════════════════════════════════════════════════════════
    """);
Console.ResetColor();

string conversationId = $"conv-{Guid.NewGuid():N}";
string? previousResponseId = null;

while (true)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("\nYou> ");
    Console.ResetColor();

    string? input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) { continue; }
    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) { break; }
    if (input.Equals("/new", StringComparison.OrdinalIgnoreCase))
    {
        conversationId = $"conv-{Guid.NewGuid():N}";
        previousResponseId = null;
        Console.WriteLine($"(conversation reset: {conversationId})");
        continue;
    }

    try
    {
        var result = await SendAsync(input, conversationId, previousResponseId);
        previousResponseId = result.ResponseId ?? previousResponseId;

        if (result.ConsentLink is { } link)
        {
            await HandleConsentAsync(link, input, conversationId);
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\nError: {ex.Message}");
        Console.ResetColor();
    }
}

Console.WriteLine("Goodbye!");

async Task HandleConsentAsync(string consentLink, string originalPrompt, string convId)
{
    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.WriteLine("\nOAuth consent required for the requested tool.");
    Console.WriteLine($"Consent URL: {consentLink}");
    Console.ResetColor();
    TryOpenBrowser(consentLink);

    Console.Write("Complete the consent in the browser, then press Enter to retry (or 'skip'): ");
    string? ans = Console.ReadLine();
    if (string.Equals(ans?.Trim(), "skip", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    Console.WriteLine("Retrying...");
    var retry = await SendAsync(originalPrompt, convId, previousResponseId);
    previousResponseId = retry.ResponseId ?? previousResponseId;

    if (retry.ConsentLink is not null)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("\nConsent still required after retry. The toolbox connection may need an operator (Foundry portal → Connections → Edit authentication → Authorize).");
        Console.ResetColor();
    }
}

async Task<RequestResult> SendAsync(string prompt, string convId, string? prevResponseId)
{
    var token = await credential.GetTokenAsync(tokenContext, CancellationToken.None);

    // We send BOTH continuity hints on every turn:
    //   1. `conversation` — primary continuity. The hosted container's AgentFrameworkResponseHandler
    //      loads/saves an AgentSession from its in-process AgentSessionStore keyed by this id, so
    //      chat history is reconstructed on each turn regardless of what the Foundry response
    //      store knows. This path NEVER touches the Foundry storage backend.
    //   2. `previous_response_id` — secondary. The AgentServer SDK orchestrator can use this to
    //      pull prior history out of the Foundry response store. It works for normal turns;
    //      on consent-flow turns it may 404 because the storage backend currently fails to
    //      persist responses carrying `oauth_consent_request` + `function_call_output` (the
    //      storage POST returns 500, Azure.Core retries the non-idempotent POST, second
    //      attempt gets 409, orchestrator surfaces `storage_error`). When that happens the
    //      `conversation` field above covers continuity transparently.
    //
    // `store` defaults to true on the server so previous_response_id retrieval is available
    // for non-consent turns; we don't override it.
    var body = new Dictionary<string, object?>
    {
        ["model"] = model,
        ["stream"] = true,
        ["conversation"] = convId,
        ["input"] = new[]
        {
            new
            {
                type = "message",
                role = "user",
                content = new[] { new { type = "input_text", text = prompt } },
            },
        },
    };
    if (prevResponseId is not null)
    {
        body["previous_response_id"] = prevResponseId;
    }

    using var request = new HttpRequestMessage(HttpMethod.Post, responsesEndpoint)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    if (!response.IsSuccessStatusCode)
    {
        string errorBody = await response.Content.ReadAsStringAsync();

        // Fallback: if the server can't find the previous response (storage_error scenario),
        // retry with conversation_id only — the in-process AgentSessionStore preserves chat
        // history so the user isn't stuck in a broken conversation.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound && prevResponseId is not null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("(previous_response_id not on server; continuing via conversation_id)");
            Console.ResetColor();
            return await SendAsync(prompt, convId, null);
        }

        throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}");
    }

    return await ConsumeSseAsync(await response.Content.ReadAsStreamAsync());
}

static async Task<RequestResult> ConsumeSseAsync(Stream stream)
{
    using var reader = new StreamReader(stream, Encoding.UTF8);
    var buffer = new StringBuilder();
    string? responseId = null;
    string? consentLink = null;
    bool agentLabelPrinted = false;

    while (await reader.ReadLineAsync() is { } line)
    {
        if (line.Length == 0)
        {
            // Dispatch buffered event.
            if (buffer.Length == 0) { continue; }
            string payload = buffer.ToString();
            buffer.Clear();

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            string evtType = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

            switch (evtType)
            {
                case "response.created":
                case "response.in_progress":
                    if (root.TryGetProperty("response", out var respEl)
                        && respEl.TryGetProperty("id", out var idEl))
                    {
                        responseId = idEl.GetString();
                    }
                    break;

                case "response.output_text.delta":
                    if (root.TryGetProperty("delta", out var deltaEl))
                    {
                        if (!agentLabelPrinted)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.Write("\nAgent> ");
                            Console.ResetColor();
                            agentLabelPrinted = true;
                        }
                        Console.Write(deltaEl.GetString());
                    }
                    break;

                case "response.output_item.added":
                    if (root.TryGetProperty("item", out var itemEl)
                        && itemEl.TryGetProperty("type", out var itemTypeEl)
                        && itemTypeEl.GetString() == "oauth_consent_request"
                        && itemEl.TryGetProperty("consent_link", out var linkEl))
                    {
                        consentLink = linkEl.GetString();
                    }
                    break;

                case "response.completed":
                case "response.incomplete":
                case "response.failed":
                    if (root.TryGetProperty("response", out var finalResp)
                        && finalResp.TryGetProperty("id", out var finalIdEl))
                    {
                        responseId = finalIdEl.GetString();
                    }
                    if (evtType == "response.failed" && root.TryGetProperty("response", out var failedResp)
                        && failedResp.TryGetProperty("error", out var errEl))
                    {
                        string? errCode = errEl.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;

                        // storage_error means the Foundry response store couldn't persist this
                        // response — known backend bug on consent-flow responses (mixed output
                        // items). Drop the response id so we don't pass an unresolvable
                        // previous_response_id next turn, but conversation continuity is
                        // preserved by the `conversation` field we always send, so this is
                        // informational, not an error from the user's perspective.
                        bool isStorageError = string.Equals(errCode, "storage_error", StringComparison.Ordinal);
                        if (isStorageError)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine("(server storage_error: response not stored on the Foundry backend; conversation context preserved via conversation_id)");
                            Console.ResetColor();
                            responseId = null;
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"\n[server error] {errEl.GetRawText()}");
                            Console.ResetColor();
                        }
                    }
                    break;
            }
        }
        else if (line.StartsWith("data:", StringComparison.Ordinal))
        {
            string data = line.Substring(5).TrimStart();
            if (data == "[DONE]") { break; }
            buffer.Append(data);
        }
        // "event:", ":", and other SSE field lines are ignored — payloads arrive on data: lines.
    }

    if (agentLabelPrinted) { Console.WriteLine(); }
    return new RequestResult(responseId, consentLink);
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

internal readonly record struct RequestResult(string? ResponseId, string? ConsentLink);
