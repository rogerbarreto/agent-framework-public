// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to use the Browser Automation Tool with a ChatClientAgent.
// The tool lets the agent drive a cloud browser hosted by a Microsoft Playwright Workspace.

using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Extensions.AI;

string browserAutomationConnectionName = Environment.GetEnvironmentVariable("BROWSER_AUTOMATION_CONNECTION_NAME") ?? throw new InvalidOperationException("BROWSER_AUTOMATION_CONNECTION_NAME is not set.");

const string AgentInstructions = """
    You are an agent helping with browser automation tasks.
    You can answer questions, provide information, and assist with various tasks
    related to web browsing using the Browser Automation tool available to you.
    """;

string endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT") ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("FOUNDRY_MODEL") ?? "gpt-5.4-mini";

// Browser automation runs can take a few minutes, so use a longer network timeout than the default.
AIProjectClientOptions clientOptions = new() { NetworkTimeout = TimeSpan.FromMinutes(5) };

// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
// latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
AIProjectClient aiProjectClient = new(new Uri(endpoint), new DefaultAzureCredential(), clientOptions);

// Look up the Browser Automation project connection by name and pass its ID to the tool.
AIProjectConnection browserAutomationConnection = await aiProjectClient.Connections.GetConnectionAsync(browserAutomationConnectionName);
var browserAutomationOptions = new BrowserAutomationToolOptions(new BrowserAutomationToolConnectionOptions(browserAutomationConnection.Id));

// Create a AIAgent with the Browser Automation tool.
AIAgent agent = aiProjectClient.AsAIAgent(deploymentName,
    instructions: AgentInstructions,
    name: "BrowserAutomationAgent-RAPI",
    tools: [FoundryAITool.CreateBrowserAutomationTool(browserAutomationOptions)]);

Console.WriteLine($"Created agent: {agent.Name}");

// Require the agent to use a tool so that it browses instead of answering from its own knowledge.
ChatClientAgentRunOptions runOptions = new()
{
    ChatOptions = new ChatOptions { ToolMode = ChatToolMode.RequireAny }
};

const string Task = """
    Your goal is to report the percent of Microsoft year-to-date stock price change.
    To do that, go to the website finance.yahoo.com.
    At the top of the page, you will find a search bar.
    Enter the value 'MSFT', to get information about the Microsoft stock price.
    At the top of the resulting page you will see a default chart of Microsoft stock price.
    Click on 'YTD' at the top of that chart, and report the percent value that shows up just below it.
    """;

// WARNING: The streamed text can include a live view URL that grants access to the browser session.
// Treat it as sensitive: only display it in a trusted terminal and redact it before sharing output or saving logs.
Console.WriteLine("\n=== Agent Response ===");
await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(Task, options: runOptions))
{
    Console.Write(update.Text);
}

Console.WriteLine();
