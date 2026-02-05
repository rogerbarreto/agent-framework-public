// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to use Microsoft Fabric Tool with AI Agents.

using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI.Responses;

string endpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_ENDPOINT") ?? throw new InvalidOperationException("AZURE_FOUNDRY_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_DEPLOYMENT_NAME") ?? "gpt-4o-mini";
string fabricConnectionId = Environment.GetEnvironmentVariable("FABRIC_PROJECT_CONNECTION_ID") ?? throw new InvalidOperationException("FABRIC_PROJECT_CONNECTION_ID is not set.");

const string AgentInstructions = "You are a helpful assistant with access to Microsoft Fabric data. Answer questions based on data available through your Fabric connection.";
const string AgentNameNative = "FabricAgent-NATIVE";

// Get a client to create/retrieve/delete server side agents with Azure Foundry Agents.
AIProjectClient aiProjectClient = new(new Uri(endpoint), new AzureCliCredential());

// Configure Microsoft Fabric tool options with project connection
var fabricToolOptions = new FabricDataAgentToolOptions();
fabricToolOptions.ProjectConnections.Add(new ToolProjectConnection(fabricConnectionId));

// Create the server side agent with Microsoft Fabric tool
AIAgent agent = await aiProjectClient.CreateAIAgentAsync(
    name: AgentNameNative,
    creationOptions: new AgentVersionCreationOptions(
        new PromptAgentDefinition(model: deploymentName)
        {
            Instructions = AgentInstructions,
            Tools =
            {
                (ResponseTool)AgentTool.CreateMicrosoftFabricTool(fabricToolOptions)
            }
        })
);

Console.WriteLine($"Created agent: {agent.Name}");

// Run the agent with a sample query
AgentResponse response = await agent.RunAsync("What data is available in the connected Fabric workspace?");

// Display the response
foreach (var message in response.Messages)
{
    foreach (var content in message.Contents)
    {
        if (content is Microsoft.Extensions.AI.TextContent textContent)
        {
            Console.WriteLine($"Agent: {textContent.Text}");
        }
    }
}

// Cleanup by agent name removes the agent version created.
await aiProjectClient.Agents.DeleteAgentAsync(agent.Name);
Console.WriteLine($"Deleted agent: {agent.Name}");
