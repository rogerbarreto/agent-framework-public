// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to use SharePoint Grounding Tool with AI Agents.

using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI.Responses;

string endpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_ENDPOINT") ?? throw new InvalidOperationException("AZURE_FOUNDRY_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_DEPLOYMENT_NAME") ?? "gpt-4o-mini";
string sharepointConnectionId = Environment.GetEnvironmentVariable("SHAREPOINT_PROJECT_CONNECTION_ID") ?? throw new InvalidOperationException("SHAREPOINT_PROJECT_CONNECTION_ID is not set.");

const string AgentInstructions = """
    You are a helpful agent that can use SharePoint tools to assist users.
    Use the available SharePoint tools to answer questions and perform tasks.
    """;
const string AgentNameMEAI = "SharePointAgent-MEAI";
const string AgentNameNative = "SharePointAgent-NATIVE";

// Get a client to create/retrieve/delete server side agents with Azure Foundry Agents.
AIProjectClient aiProjectClient = new(new Uri(endpoint), new AzureCliCredential());

// Create SharePoint tool options with project connection
var sharepointOptions = new SharePointGroundingToolOptions();
sharepointOptions.ProjectConnections.Add(new ToolProjectConnection(sharepointConnectionId));

// Option 1 - Using AgentTool.CreateSharepointTool + AsAITool() (MEAI + AgentFramework)
AIAgent agentOption1 = await aiProjectClient.CreateAIAgentAsync(
    model: deploymentName,
    name: AgentNameMEAI,
    instructions: AgentInstructions,
    tools: [((ResponseTool)AgentTool.CreateSharepointTool(sharepointOptions)).AsAITool()]);

// Option 2 - Using PromptAgentDefinition SDK native type
AIAgent agentOption2 = await aiProjectClient.CreateAIAgentAsync(
    name: AgentNameNative,
    creationOptions: new AgentVersionCreationOptions(
        new PromptAgentDefinition(model: deploymentName)
        {
            Instructions = AgentInstructions,
            Tools = { AgentTool.CreateSharepointTool(sharepointOptions) }
        })
);

// Either invoke option1 or option2 agent, should have same result
// Option 1
AgentResponse response = await agentOption1.RunAsync("List the documents available in SharePoint");

// Option 2
// AgentResponse response = await agentOption2.RunAsync("List the documents available in SharePoint");

// Display the response
Console.WriteLine($"Agent Response: {response}");

// Display grounding annotations if any
foreach (var message in response.Messages)
{
    foreach (var content in message.Contents)
    {
        if (content.Annotations is not null)
        {
            foreach (var annotation in content.Annotations)
            {
                Console.WriteLine($"Annotation: {annotation}");
            }
        }
    }
}

// Cleanup by agent name removes the agent version created.
await aiProjectClient.Agents.DeleteAgentAsync(agentOption1.Name);
await aiProjectClient.Agents.DeleteAgentAsync(agentOption2.Name);
