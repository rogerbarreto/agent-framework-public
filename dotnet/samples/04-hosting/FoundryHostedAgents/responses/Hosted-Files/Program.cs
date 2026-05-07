// Copyright (c) Microsoft. All rights reserved.

// Hosted Files Agent - A hosted agent that reads files from the per-session
// $HOME sandbox volume using local C# function tools.
//
// In Foundry hosted-agent mode, every session is backed by an isolated
// micro-VM with a persistent $HOME directory. Files uploaded to the session
// (via the AgentSessionFiles SDK or `azd ai agent files upload`) appear under
// $HOME and can be read by tools running inside the agent process.
//
// Required environment variables:
//   AZURE_AI_PROJECT_ENDPOINT         - Azure AI Foundry project endpoint
//   AZURE_AI_MODEL_DEPLOYMENT_NAME    - Model deployment name (default: gpt-4o)
//
// Optional:
//   AGENT_NAME                        - Agent name (default: hosted-files)

using System.ComponentModel;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;

// Load .env file if present (for local development)
Env.TraversePath().Load();

string endpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME") ?? "gpt-4o";

// Use a chained credential: try a temporary dev token first (for local Docker debugging),
// then fall back to DefaultAzureCredential (for local dev via dotnet run / managed identity in production).
TokenCredential credential = new ChainedTokenCredential(
    new DevTemporaryTokenCredential(),
    new DefaultAzureCredential());

// ── Tools: read files from the session $HOME volume ──────────────────────────

// $HOME resolves to the per-session sandbox volume on Foundry. Locally it
// resolves to the OS user profile, which lets the sample run unmodified
// during development.
string Home() =>
    Environment.GetEnvironmentVariable("HOME")
    ?? Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

[Description("Get the absolute path of the session home directory ($HOME).")]
string GetHomeDirectory() => Home();

[Description("List files and directories under the given path inside the session sandbox. Pass an empty string to list $HOME.")]
string[] ListFiles(
    [Description("Path relative to $HOME (or absolute). Empty string means $HOME.")] string path)
{
    try
    {
        string target = ResolveSessionPath(path);
        return Directory.EnumerateFileSystemEntries(target).ToArray();
    }
    catch (Exception ex)
    {
        return [$"Error listing '{path}': {ex.Message}"];
    }
}

[Description("Read the full text contents of a file inside the session sandbox.")]
string ReadFile(
    [Description("Path relative to $HOME (or absolute) of the file to read.")] string path)
{
    try
    {
        string target = ResolveSessionPath(path);
        return File.ReadAllText(target);
    }
    catch (Exception ex)
    {
        return $"Error reading '{path}': {ex.Message}";
    }
}

string ResolveSessionPath(string path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return Home();
    }

    return Path.IsPathRooted(path) ? path : Path.Combine(Home(), path);
}

// ── Create and host the agent ────────────────────────────────────────────────

AIAgent agent = new AIProjectClient(new Uri(endpoint), credential)
    .AsAIAgent(
        model: deploymentName,
        instructions: """
            You are a friendly assistant that helps users inspect and summarise
            files stored in the session sandbox at $HOME.

            Always answer file-related questions by calling the available tools
            (GetHomeDirectory, ListFiles, ReadFile). Do not guess file paths or
            contents — read the file before answering.

            Quote numbers and figures verbatim from the file rather than
            paraphrasing them.
            """,
        name: Environment.GetEnvironmentVariable("AGENT_NAME") ?? "hosted-files",
        description: "Hosted agent that reads files from the per-session $HOME volume",
        tools:
        [
            AIFunctionFactory.Create(GetHomeDirectory),
            AIFunctionFactory.Create(ListFiles),
            AIFunctionFactory.Create(ReadFile),
        ]);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFoundryResponses(agent);

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

        return new AccessToken(this._token, DateTimeOffset.UtcNow.AddHours(1));
    }
}
