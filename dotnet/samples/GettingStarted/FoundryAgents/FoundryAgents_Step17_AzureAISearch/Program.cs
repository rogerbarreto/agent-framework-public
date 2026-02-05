// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to use Azure AI Search Tool with AI Agents.

using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

string endpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_ENDPOINT") ?? throw new InvalidOperationException("AZURE_FOUNDRY_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_DEPLOYMENT_NAME") ?? "gpt-4o-mini";
string searchConnectionId = Environment.GetEnvironmentVariable("AI_SEARCH_PROJECT_CONNECTION_ID") ?? throw new InvalidOperationException("AI_SEARCH_PROJECT_CONNECTION_ID is not set.");
string searchIndexName = Environment.GetEnvironmentVariable("AI_SEARCH_INDEX_NAME") ?? throw new InvalidOperationException("AI_SEARCH_INDEX_NAME is not set.");

const string AgentInstructions = """
    You are a helpful assistant. You must always provide citations for
    answers using the tool and render them as: `[message_idx:search_idx†source]`.
    """;

const string AgentName = "SearchAgent";

// Get a client to create/retrieve/delete server side agents with Azure Foundry Agents.
AIProjectClient aiProjectClient = new(new Uri(endpoint), new AzureCliCredential());

// Create the Azure AI Search tool with the configured index
var searchToolIndex = new AzureAISearchToolIndex
{
    IndexName = searchIndexName,
    ProjectConnectionId = searchConnectionId,
};

var searchTool = AgentTool.CreateAzureAISearchTool(new AzureAISearchToolOptions([searchToolIndex]));

// Create the server side agent with Azure AI Search tool
AIAgent agent = await aiProjectClient.CreateAIAgentAsync(
    name: AgentName,
    creationOptions: new AgentVersionCreationOptions(
        new PromptAgentDefinition(model: deploymentName)
        {
            Instructions = AgentInstructions,
            Tools = { searchTool }
        })
);

// Run the agent with a search query
AgentResponse response = await agent.RunAsync("What information do you have in the search index?");

// Display the response
Console.WriteLine("Agent Response:");
foreach (ChatMessage message in response.Messages)
{
    foreach (AIContent content in message.Contents)
    {
        if (content is TextContent textContent)
        {
            Console.WriteLine(textContent.Text);
        }
    }
}

// Display any citations/annotations
foreach (AIAnnotation annotation in response.Messages.SelectMany(m => m.Contents).SelectMany(c => c.Annotations ?? []))
{
    Console.WriteLine($"Citation: {annotation}");
}

// Cleanup by deleting the agent
await aiProjectClient.Agents.DeleteAgentAsync(agent.Name);

Console.WriteLine("\nAgent deleted successfully.");
