// Copyright (c) Microsoft. All rights reserved.

// Browser Automation via a Foundry Toolbox (MCP, Streamable HTTP).
//
// Create a Foundry Toolbox that contains the Browser Automation tool, then point
// an `McpClient` at the toolbox's MCP endpoint. The agent discovers the toolbox's
// tools at runtime and invokes them over MCP.

using System.ClientModel;
using System.Net.Http.Headers;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

#pragma warning disable OPENAI001 // Experimental API
#pragma warning disable AAIP001  // AgentToolboxes is experimental

// Name of the toolbox to create and connect to.
const string ToolboxName = "browser_automation_toolbox";
const string Query = """
    Your goal is to report the percent of Microsoft year-to-date stock price change.
    To do that, go to the website finance.yahoo.com.
    At the top of the page, you will find a search bar.
    Enter the value 'MSFT', to get information about the Microsoft stock price.
    At the top of the resulting page you will see a default chart of Microsoft stock price.
    Click on 'YTD' at the top of that chart, and report the percent value that shows up just below it.
    """;

string endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("FOUNDRY_MODEL") ?? "gpt-5.4-mini";
string browserAutomationConnectionName = Environment.GetEnvironmentVariable("BROWSER_AUTOMATION_CONNECTION_NAME")
    ?? throw new InvalidOperationException("BROWSER_AUTOMATION_CONNECTION_NAME is not set.");

TokenCredential credential = new DefaultAzureCredential();

// Browser automation runs can take a few minutes, so use a longer network timeout than the default.
AIProjectClientOptions clientOptions = new() { NetworkTimeout = TimeSpan.FromMinutes(5) };

// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
// latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
AIProjectClient aiProjectClient = new(new Uri(endpoint), credential, clientOptions);

// Look up the Browser Automation project connection by name.
AIProjectConnection browserAutomationConnection = await aiProjectClient.Connections.GetConnectionAsync(browserAutomationConnectionName);

// Comment out if the toolbox already exists in your Foundry project.
var toolboxEndpoint = await CreateBrowserAutomationToolboxAsync(ToolboxName, endpoint, credential, browserAutomationConnection.Id);

// Inject a fresh Azure AI bearer token on every MCP request.
using var httpClient = new HttpClient(new BearerTokenHandler(credential, "https://ai.azure.com/.default")
{
    InnerHandler = new HttpClientHandler(),
})
{
    Timeout = TimeSpan.FromMinutes(5),
};

Console.WriteLine($"Connecting to toolbox MCP endpoint: {toolboxEndpoint}");

await using McpClient mcpClient = await McpClient.CreateAsync(
    new HttpClientTransport(
        new HttpClientTransportOptions
        {
            Endpoint = new Uri(toolboxEndpoint),
            Name = "foundry_toolbox",
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Foundry-Features"] = "Toolboxes=V1Preview",
            },
        },
        httpClient));

IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
Console.WriteLine($"Toolbox MCP tools available: {string.Join(", ", mcpTools.Select(t => t.Name))}");

AIAgent agent = aiProjectClient.AsAIAgent(
    model: deploymentName,
    instructions: "You are an agent helping with browser automation tasks. Use the available toolbox tools to browse the web and answer the user.",
    name: "BrowserAutomationToolboxAgent",
    tools: [.. mcpTools.Cast<AITool>()]);

// WARNING: The response can include a live view URL that grants access to the browser session.
// Treat it as sensitive: only display it in a trusted terminal and redact it before sharing output or saving logs.
Console.WriteLine($"\nUser: {Query}\n");
Console.WriteLine($"Assistant: {await agent.RunAsync(Query)}");

// ---------------------------------------------------------------------------
// Helper: create (or replace) a toolbox with the Browser Automation tool
// ---------------------------------------------------------------------------
static async Task<string> CreateBrowserAutomationToolboxAsync(string name, string endpoint, TokenCredential credential, string browserAutomationConnectionId)
{
    // Toolboxes are normally configured in the Foundry portal or a deployment
    // script, not the application itself. This helper exists so the sample can
    // be run end-to-end without first setting a toolbox up by hand.

    var adminClient = new AgentAdministrationClient(new Uri(endpoint), credential);
    var toolboxClient = adminClient.GetAgentToolboxes();

    // Delete existing toolbox if present (ignore 404).
    try
    {
        await toolboxClient.DeleteAsync(name);
        Console.WriteLine($"Deleted existing toolbox '{name}'");
    }
    catch (ClientResultException ex) when (ex.Status == 404)
    {
        // Toolbox does not exist — nothing to delete.
    }

    // Create a fresh version with the Browser Automation tool, bound to the Browser Automation project connection.
    BrowserAutomationToolboxTool browserAutomationTool = new(
        new BrowserAutomationToolOptions(new BrowserAutomationToolConnectionOptions(browserAutomationConnectionId)));

    ToolboxVersion created = (await toolboxClient.CreateVersionAsync(
        name: name,
        tools: [browserAutomationTool],
        description: "Sample toolbox with the Browser Automation tool — created by Agent_Step28 sample.")).Value;

    Console.WriteLine($"Created toolbox '{created.Name}' v{created.Version} ({created.Tools.Count} tool(s))");
    return $"{endpoint}/toolboxes/{created.Name}/mcp?api-version=v{created.Version}";
}

// ---------------------------------------------------------------------------
// DelegatingHandler: attaches a fresh Azure AI bearer token to every request
// ---------------------------------------------------------------------------
internal sealed class BearerTokenHandler(TokenCredential credential, string scope) : DelegatingHandler
{
    private readonly TokenRequestContext _tokenContext = new([scope]);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AccessToken token = await credential.GetTokenAsync(this._tokenContext, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
