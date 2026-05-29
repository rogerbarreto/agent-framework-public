// Copyright (c) Microsoft. All rights reserved.

// Foundry Toolbox Auth Paths Agent — A hosted agent backed by a single Foundry Toolbox
// that bundles MCP tools using FOUR different authentication paths.
//
// This sample demonstrates the same hosting bones as Hosted-Toolbox/, but the toolbox
// (provisioned by the user out-of-band) contains four MCP tool entries each authenticated
// differently. The agent code itself is agnostic to authentication — the educational
// surface lives in the toolbox configuration in the Foundry portal and in this sample's
// README.md.
//
// Required environment variables:
//   AZURE_AI_PROJECT_ENDPOINT (local-dev) OR FOUNDRY_PROJECT_ENDPOINT (hosted runtime)
//                                     - Azure AI Foundry project endpoint. The Foundry hosted
//                                       runtime auto-injects FOUNDRY_PROJECT_ENDPOINT; locally
//                                       set AZURE_AI_PROJECT_ENDPOINT (the AF-repo convention).
//   TOOLBOX_NAME                      - Name of the Foundry Toolbox to load
//                                       (default: auth-paths-toolbox)
//
// Optional:
//   AZURE_AI_MODEL_DEPLOYMENT_NAME    - Model deployment name (default: gpt-4o)
//   AGENT_NAME                        - Defaults to "hosted-toolbox-auth-paths-agent".
//
// The Foundry.Hosting package builds the toolbox proxy URL from FOUNDRY_PROJECT_ENDPOINT
// per tools-integration-spec.md §2–§3, so the sample does not need to plumb any
// toolbox-specific URL env var.
//
// NOTE: All FOUNDRY_* and AGENT_* env-var prefixes (other than the platform-injected ones
// listed above) are reserved by the Foundry container platform and rejected by the
// agent-create API. Use TOOLBOX_NAME, not FOUNDRY_TOOLBOX_NAME, for sample-owned config.

#pragma warning disable OPENAI001 // FoundryAITool.CreateHostedMcpToolbox is experimental

using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;

// Load .env file if present (for local development)
Env.TraversePath().Load();

// Project endpoint resolution order:
//   1. FOUNDRY_PROJECT_ENDPOINT — auto-injected by the Foundry hosted runtime.
//   2. AZURE_AI_PROJECT_ENDPOINT — the convention developers set locally for `dotnet run`.
// When deployed, only (1) is available; the AF-repo sample convention to set (2) at
// deploy time fails silently because the platform reserves all FOUNDRY_* env-var names
// and rejects them at agent-create time. Read both, prefer the platform-injected one.
string endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException(
        "Neither FOUNDRY_PROJECT_ENDPOINT (platform-injected in hosted runtime) " +
        "nor AZURE_AI_PROJECT_ENDPOINT (local-dev convention) is set.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME") ?? "gpt-4o";
string toolboxName = Environment.GetEnvironmentVariable("TOOLBOX_NAME") ?? "auth-paths-toolbox";
string oauthToolboxName = Environment.GetEnvironmentVariable("OAUTH_TOOLBOX_NAME") ?? "auth-paths-oauth-toolbox";
string agentName = Environment.GetEnvironmentVariable("AGENT_NAME") ?? "hosted-toolbox-auth-paths-agent";

TokenCredential credential = new ChainedTokenCredential(
    new DevTemporaryTokenCredential(),
    new DefaultAzureCredential());

// Notes on toolbox wiring:
//   - The toolbox tools are added at request time by AgentFrameworkResponseHandler from
//     FoundryToolboxService.Tools (the pre-registered set). Do NOT pass the toolbox
//     marker (FoundryAITool.CreateHostedMcpToolbox) in the agent's `tools:` array — that
//     marker is for the per-request scenario where the CALLER specifies the toolbox in
//     the request body. Server-side baked-in toolbox uses the AddFoundryToolboxes path.
AIAgent agent = new AIProjectClient(new Uri(endpoint), credential)
    .AsAIAgent(
        model: deploymentName,
        instructions: """
            You are a helpful assistant with access to several tools, each provided by a different
            upstream service authenticated through a distinct mechanism (API key, agent managed
            identity, custom OAuth user identity, and a literal token shipped with the tool
            definition). Pick the tool that best fits the user's question and explain which
            upstream service answered when you respond.
            """,
        name: agentName,
        description: "Hosted agent demonstrating four MCP-tool authentication paths via a Foundry Toolbox.");

// Tier 3 spine (WebApplication.CreateBuilder + AddFoundryResponses + MapFoundryResponses):
// the Foundry.Hosting package auto-maps the spec-required GET /readiness probe inside
// MapFoundryResponses (idempotent — skipped when AgentHost or the developer already
// mapped it), so the sample stays free of platform plumbing.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFoundryResponses(agent);
// Register the same credential used to build the agent so that
// FoundryToolboxBearerTokenHandler picks it up (it defaults to a fresh
// DefaultAzureCredential when none is registered, which fails for local
// Docker debugging where AZURE_BEARER_TOKEN is the only available source).
builder.Services.AddSingleton(credential);
// Pre-register the toolbox name so FoundryToolboxService resolves the foundry-toolbox://
// marker at request time. With FOUNDRY_PROJECT_ENDPOINT injected by the platform, startup
// MCP tools/list against the toolbox proxy is typically <100ms in-region.
// Pre-register the eager toolbox (3 paths: API key, agent MI, inline token). Then register
// the OAuth user-identity toolbox as a LAZY toolbox — its tools/list cannot succeed under
// the hosted agent's MI (a service principal that cannot do interactive consent), so we
// defer the MCP connection until the model actually invokes one of the declared tools.
// The first invocation runs under the agent's MI (Foundry toolbox proxy resolves
// connection credentials server-side) and any -32007 CONSENT_REQUIRED is surfaced
// to the client as an `oauth_consent_request` output item per
// `Microsoft.Agents.AI.Foundry.Hosting.FoundryConsentErrorHelper` (the .NET parity
// with `agent_framework_foundry_hosting/_responses.py:CONSENT_ERROR_CODE`).
builder.Services.AddFoundryToolboxes(
    options =>
    {
        options.LazyToolboxNames.Add(oauthToolboxName);

        var openObjectSchema = System.Text.Json.JsonDocument.Parse(
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":true}").RootElement;

        options.LazyToolDescriptors.Add(new()
        {
            ToolboxName = oauthToolboxName,
            ToolName = "github_oauth___search_issues",
            Description = "Search GitHub issues as the calling end-user via the OAuth-connected GitHub MCP server.",
            JsonSchema = openObjectSchema,
        });
        options.LazyToolDescriptors.Add(new()
        {
            ToolboxName = oauthToolboxName,
            ToolName = "github_oauth___issue_read",
            Description = "Read a GitHub issue as the calling end-user via the OAuth-connected GitHub MCP server.",
            JsonSchema = openObjectSchema,
        });
        options.LazyToolDescriptors.Add(new()
        {
            ToolboxName = oauthToolboxName,
            ToolName = "github_oauth___get_me",
            Description = "Return the authenticated GitHub user's profile via the OAuth-connected GitHub MCP server.",
            JsonSchema = openObjectSchema,
        });
    },
    toolboxName);

var app = builder.Build();
app.MapFoundryResponses();

if (app.Environment.IsDevelopment())
{
    app.MapFoundryResponses("openai/v1");
}

app.Run();

// ── DevTemporaryTokenCredential ───────────────────────────────────────────────

/// <summary>
/// A <see cref="TokenCredential"/> for local Docker debugging only.
/// Reads a pre-fetched bearer token from the <c>AZURE_BEARER_TOKEN</c> environment variable
/// once at startup. This should NOT be used in production.
///
/// Generate a token on your host and pass it to the container:
///   export AZURE_BEARER_TOKEN=$(az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv)
///   docker run -e AZURE_BEARER_TOKEN=$AZURE_BEARER_TOKEN ...
/// </summary>
internal sealed class DevTemporaryTokenCredential : TokenCredential
{
    private const string EnvironmentVariable = "AZURE_BEARER_TOKEN";
    private readonly string? _token;

    public DevTemporaryTokenCredential()
    {
        this._token = Environment.GetEnvironmentVariable(EnvironmentVariable);
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => this.GetAccessToken();

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => new(this.GetAccessToken());

    private AccessToken GetAccessToken()
    {
        if (string.IsNullOrEmpty(this._token) || this._token == "DefaultAzureCredential")
        {
            throw new CredentialUnavailableException($"{EnvironmentVariable} environment variable is not set.");
        }

        return new AccessToken(this._token, DateTimeOffset.MaxValue);
    }
}
