// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to use Browser Automation Tool with AI Agents.

using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

string endpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_ENDPOINT") ?? throw new InvalidOperationException("AZURE_FOUNDRY_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_DEPLOYMENT_NAME") ?? "gpt-4o-mini";
string connectionId = Environment.GetEnvironmentVariable("BROWSER_AUTOMATION_PROJECT_CONNECTION_ID") ?? throw new InvalidOperationException("BROWSER_AUTOMATION_PROJECT_CONNECTION_ID is not set.");

const string AgentInstructions = """
    You are an Agent helping with browser automation tasks.
    You can answer questions, provide information, and assist with various tasks
    related to web browsing using the Browser Automation tool available to you.
    """;

const string AgentNameNative = "BrowserAutomationAgent-NATIVE";

// Get a client to create/retrieve/delete server side agents with Azure Foundry Agents.
AIProjectClient aiProjectClient = new(new Uri(endpoint), new AzureCliCredential());

// Create the server side agent using PromptAgentDefinition with Browser Automation tool
AIAgent agent = await aiProjectClient.CreateAIAgentAsync(
    name: AgentNameNative,
    creationOptions: new AgentVersionCreationOptions(
        new PromptAgentDefinition(model: deploymentName)
        {
            Instructions = AgentInstructions,
            Tools =
            {
                AgentTool.CreateBrowserAutomationTool(
                    new BrowserAutomationToolParameters(
                        new BrowserAutomationToolConnectionParameters(connectionId)))
            }
        })
);

Console.WriteLine($"Agent created with ID: {agent.Name}");

// Query the agent to perform a browser automation task
string query = """
    Your goal is to report the percent of Microsoft year-to-date stock price change.
    To do that, go to the website finance.yahoo.com, search for the Microsoft stock symbol MSFT,
    and report the year-to-date percentage change in the stock price.
    """;

Console.WriteLine($"User: {query}");
Console.WriteLine();

AgentResponse response = await agent.RunAsync(query);

// Display the response
foreach (ChatMessage message in response.Messages)
{
    foreach (AIContent content in message.Contents)
    {
        if (content is TextContent textContent)
        {
            Console.WriteLine($"Agent: {textContent.Text}");
        }
    }
}

// Cleanup by agent name removes the agent version created.
await aiProjectClient.Agents.DeleteAgentAsync(agent.Name);
Console.WriteLine($"\nAgent '{agent.Name}' deleted.");
