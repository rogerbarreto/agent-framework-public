// Copyright (c) Microsoft. All rights reserved.

// Hosted Files Agent - A hosted agent that exposes file content baked into the
// container image as knowledge accessible through three local C# tools.
//
// The contents of the project's `resources/` folder are copied into the
// published output (see HostedFiles.csproj) and live at `/app/resources/`
// inside the container at runtime. The agent's tools read files from that
// directory and surface their contents to the model.
//
// Required environment variables:
//   AZURE_AI_PROJECT_ENDPOINT         - Azure AI Foundry project endpoint
//   AZURE_AI_MODEL_DEPLOYMENT_NAME    - Model deployment name (default: gpt-4o)
//
// Optional:
//   AGENT_NAME                        - Agent name (default: hosted-files)
//   RESOURCES_DIR                     - Override the data directory the tools
//                                       read from (default: <baseDir>/resources)

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

// Bypass SampleEnvironment alias (which prompts on missing env vars) for optional values.
string? GetOptionalEnv(string key) => System.Environment.GetEnvironmentVariable(key);

string endpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is not set.");
string deploymentName = GetOptionalEnv("AZURE_AI_MODEL_DEPLOYMENT_NAME") ?? "gpt-4o";

// Use a chained credential: try a temporary dev token first (for local Docker debugging),
// then fall back to DefaultAzureCredential (for local dev via dotnet run / managed identity in production).
TokenCredential credential = new ChainedTokenCredential(
    new DevTemporaryTokenCredential(),
    new DefaultAzureCredential());

// ── Resources directory (baked into the image) ──────────────────────────────

// Defaults to <process-base-dir>/resources, which is where the csproj's
// CopyToOutputDirectory entries land. Inside the container that resolves to
// /app/resources/. Override via RESOURCES_DIR if needed.
string resourcesDir = GetOptionalEnv("RESOURCES_DIR")
    ?? Path.Combine(AppContext.BaseDirectory, "resources");

// ── Tools: read files from the agent's bundled data directory ────────────────

[Description("List the names of files available to the agent, one per line.")]
string ListFiles()
{
    try
    {
        if (!Directory.Exists(resourcesDir))
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(resourcesDir).Select(Path.GetFileName));
    }
    catch (Exception ex)
    {
        return $"Error listing files: {ex.Message}";
    }
}

[Description("Read the full text contents of a file by name.")]
string ReadFile(
    [Description("Name of the file to read (as returned by ListFiles).")] string fileName)
{
    try
    {
        string fullPath = Path.Combine(resourcesDir, Path.GetFileName(fileName));
        return File.Exists(fullPath)
            ? File.ReadAllText(fullPath)
            : $"File '{fileName}' not found.";
    }
    catch (Exception ex)
    {
        return $"Error reading '{fileName}': {ex.Message}";
    }
}

// ── Create and host the agent ────────────────────────────────────────────────

AIAgent agent = new AIProjectClient(new Uri(endpoint), credential)
    .AsAIAgent(
        model: deploymentName,
        instructions: """
            You are a friendly assistant that answers questions about a small set of
            files bundled with you.

            Always discover the available files with the ListFiles tool first, then
            use ReadFile to read the file you need before answering. Quote numbers
            and figures verbatim from the file rather than paraphrasing them.
            """,
        name: GetOptionalEnv("AGENT_NAME") ?? "hosted-files",
        description: "Hosted agent that answers questions over a small set of bundled files.",
        tools:
        [
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
        this._token = System.Environment.GetEnvironmentVariable(EnvironmentVariable);
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
