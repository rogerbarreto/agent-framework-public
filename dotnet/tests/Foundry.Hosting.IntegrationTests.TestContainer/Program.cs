// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using Azure;
using Azure.AI.Projects;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;

// Foundry hosted agent test container for Foundry.Hosting.IntegrationTests.
//
// One image, many scenarios. The IT_SCENARIO environment variable selects which agent
// behavior is wired up at startup. Each scenario corresponds to one test fixture and
// one set of tests in the IT project.
//
// The platform injects FOUNDRY_PROJECT_ENDPOINT, FOUNDRY_AGENT_NAME, FOUNDRY_AGENT_VERSION,
// PORT, and APPLICATIONINSIGHTS_CONNECTION_STRING. We never set FOUNDRY_* or AGENT_* names
// from the test side because they are reserved by the platform.

var scenario = Environment.GetEnvironmentVariable("IT_SCENARIO") ?? "happy-path";
var projectEndpoint = new Uri(Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set."));
var deployment = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME") ?? "gpt-4o";

var projectClient = new AIProjectClient(projectEndpoint, new DefaultAzureCredential());

AIAgent agent = scenario switch
{
    "happy-path" => CreateHappyPathAgent(projectClient, deployment),
    "tool-calling" => CreateToolCallingAgent(projectClient, deployment),
    "tool-calling-approval" => CreateToolCallingApprovalAgent(projectClient, deployment),
    "toolbox" => CreateToolboxAgent(projectClient, deployment),
    "mcp-toolbox" => CreateMcpToolboxAgent(projectClient, deployment),
    "custom-storage" => CreateCustomStorageAgent(projectClient, deployment),
    "azure-search-rag" => CreateAzureSearchRagAgent(projectClient, deployment),
    _ => throw new InvalidOperationException($"Unknown IT_SCENARIO '{scenario}'.")
};

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://+:{port}");
}

builder.Services.AddFoundryResponses(agent);

var app = builder.Build();
app.MapFoundryResponses();
app.MapGet("/readiness", () => Results.Ok());
app.Run();

static AIAgent CreateHappyPathAgent(AIProjectClient client, string deployment) =>
    client.AsAIAgent(
        model: deployment,
        instructions: "You are a helpful AI assistant. Always reply with exactly the single word ECHO unless the user explicitly asks a question that requires a different answer.",
        name: "happy-path-agent",
        description: "Round trip and conversation test agent.");

static AIAgent CreateToolCallingAgent(AIProjectClient client, string deployment) =>
    client.AsAIAgent(
        model: deployment,
        instructions: "You are a helpful assistant. Use the GetUtcNow and Multiply tools when appropriate.",
        name: "tool-calling-agent",
        description: "Server side tool calling test agent.",
        tools: [
            AIFunctionFactory.Create(GetUtcNow),
            AIFunctionFactory.Create(Multiply)
        ]);

static AIAgent CreateToolCallingApprovalAgent(AIProjectClient client, string deployment) =>
    // TODO: wire approval required AIFunction once the public surface is finalized.
    client.AsAIAgent(
        model: deployment,
        instructions: "You are a helpful assistant. Use the SendEmail tool when asked to send a message; it requires user approval before running.",
        name: "tool-calling-approval-agent",
        description: "Approval flow test agent (placeholder).",
        tools: [
            AIFunctionFactory.Create(SendEmail)
        ]);

static AIAgent CreateToolboxAgent(AIProjectClient client, string deployment) =>
    // TODO: wire Foundry toolbox host once API surface is finalized for hosted agents.
    client.AsAIAgent(
        model: deployment,
        instructions: "You are a toolbox enabled assistant. Use GetEnvironmentName when asked.",
        name: "toolbox-agent",
        description: "Toolbox test agent (placeholder).",
        tools: [
            AIFunctionFactory.Create(GetEnvironmentName)
        ]);

static AIAgent CreateMcpToolboxAgent(AIProjectClient client, string deployment) =>
    // TODO: wire MCP toolbox client to https://learn.microsoft.com/api/mcp.
    client.AsAIAgent(
        model: deployment,
        instructions: "You are an assistant with access to Microsoft Learn documentation via MCP.",
        name: "mcp-toolbox-agent",
        description: "MCP toolbox test agent (placeholder).");

static AIAgent CreateCustomStorageAgent(AIProjectClient client, string deployment) =>
    // TODO: substitute custom IResponsesStorageProvider in DI.
    client.AsAIAgent(
        model: deployment,
        instructions: "You are a helpful assistant.",
        name: "custom-storage-agent",
        description: "Custom storage test agent (placeholder).");

static AIAgent CreateAzureSearchRagAgent(AIProjectClient client, string deployment)
{
    // The fixture (AzureSearchRagHostedAgentFixture) injects AZURE_SEARCH_ENDPOINT and
    // AZURE_SEARCH_INDEX_NAME into the hosted agent definition. The index is provisioned
    // out of band (see dotnet/tests/Foundry.Hosting.IntegrationTests/README.md for the
    // required schema and seed content); the container only needs read access. The
    // agent's managed identity must hold 'Search Index Data Reader' on the search service
    // scope.
    var searchEndpoint = new Uri(Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT")
        ?? throw new InvalidOperationException("AZURE_SEARCH_ENDPOINT is not set for IT_SCENARIO=azure-search-rag."));
    var indexName = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX_NAME")
        ?? throw new InvalidOperationException("AZURE_SEARCH_INDEX_NAME is not set for IT_SCENARIO=azure-search-rag.");

    var searchClient = new SearchClient(searchEndpoint, indexName, new DefaultAzureCredential());

    var options = new TextSearchProviderOptions
    {
        SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
        RecentMessageMemoryLimit = 6,
    };

    return client.AsAIAgent(new ChatClientAgentOptions
    {
        Name = "azure-search-rag-agent",
        ChatOptions = new ChatOptions
        {
            ModelId = deployment,
            Instructions = "You are a helpful support specialist for Contoso Outdoors. " +
                           "Answer questions using the provided context and cite the source document when available.",
        },
        AIContextProviders = [new TextSearchProvider(CreateAzureSearchAdapter(searchClient), options)]
    });
}

static Func<string, CancellationToken, Task<IEnumerable<TextSearchProvider.TextSearchResult>>>
    CreateAzureSearchAdapter(SearchClient client, int top = 3) =>
    async (query, cancellationToken) =>
    {
        var searchOptions = new SearchOptions { Size = top };
        Response<SearchResults<SearchDocument>> response =
            await client.SearchAsync<SearchDocument>(query, searchOptions, cancellationToken).ConfigureAwait(false);

        var results = new List<TextSearchProvider.TextSearchResult>();
        await foreach (SearchResult<SearchDocument> hit in response.Value.GetResultsAsync().ConfigureAwait(false))
        {
            results.Add(new TextSearchProvider.TextSearchResult
            {
                SourceName = hit.Document.TryGetValue("sourceName", out var name) ? name?.ToString() ?? string.Empty : string.Empty,
                SourceLink = hit.Document.TryGetValue("sourceLink", out var link) ? link?.ToString() ?? string.Empty : string.Empty,
                Text = hit.Document.TryGetValue("content", out var content) ? content?.ToString() ?? string.Empty : string.Empty,
                RawRepresentation = hit
            });
        }

        return results;
    };

[Description("Returns the current UTC date and time as an ISO 8601 string.")]
static string GetUtcNow() => DateTime.UtcNow.ToString("o");

[Description("Multiplies two integers and returns the product.")]
static int Multiply([Description("First operand")] int a, [Description("Second operand")] int b) => a * b;

[Description("Sends an email. Requires user approval.")]
static string SendEmail(
    [Description("Recipient address")] string to,
    [Description("Email subject")] string subject) =>
    $"Email sent to {to} with subject '{subject}'.";

[Description("Returns the deployment environment name.")]
static string GetEnvironmentName() => "integration-test";
