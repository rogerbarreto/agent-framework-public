// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to use Content Filtering (RAI - Responsible AI) with AI Agents.

using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;

string endpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_ENDPOINT") ?? throw new InvalidOperationException("AZURE_FOUNDRY_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

// RAI policy name: Full Azure resource ID of the content filter policy
// Format: /subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.CognitiveServices/accounts/{accountName}/raiPolicies/{policyName}
string raiPolicyName = Environment.GetEnvironmentVariable("RAI_POLICY_NAME")
    ?? throw new InvalidOperationException("RAI_POLICY_NAME is not set. Set it to your RAI policy resource ID from Azure AI Foundry.");

const string AgentInstructions = "You are a helpful assistant that provides safe and appropriate responses.";
const string AgentName = "ContentFilteredAgent";

// Get a client to create/retrieve/delete server side agents with Azure Foundry Agents.
AIProjectClient aiProjectClient = new(new Uri(endpoint), new AzureCliCredential());

// Create a ContentFilterConfiguration with your RAI policy
ContentFilterConfiguration contentFilterConfig = new(raiPolicyName);

// Create a PromptAgentDefinition with content filtering enabled
PromptAgentDefinition agentDefinition = new(model: deploymentName)
{
    Instructions = AgentInstructions,
    ContentFilterConfiguration = contentFilterConfig
};

// Create the agent version with content filtering
AgentVersionCreationOptions creationOptions = new(agentDefinition);
AgentVersion createdAgentVersion = aiProjectClient.Agents.CreateAgentVersion(agentName: AgentName, creationOptions);

Console.WriteLine($"Created agent '{AgentName}' with content filtering enabled.");
Console.WriteLine($"Agent Version: {createdAgentVersion.Version}");
Console.WriteLine($"RAI Policy: {raiPolicyName}");
Console.WriteLine();

// Use the agent
AIAgent agent = aiProjectClient.AsAIAgent(createdAgentVersion);

// Test with a normal query
string normalQuery = "What is the capital of France?";
Console.WriteLine($"User: {normalQuery}");
AgentResponse response = await agent.RunAsync(normalQuery);
Console.WriteLine($"Agent: {response}");
Console.WriteLine();

// Test with another query - content filtering is applied based on your RAI policy
string anotherQuery = "Tell me about responsible AI practices.";
Console.WriteLine($"User: {anotherQuery}");
try
{
    response = await agent.RunAsync(anotherQuery);
    Console.WriteLine($"Agent: {response}");
}
catch (Exception ex)
{
    // Content filter may trigger for certain inputs depending on your policy configuration
    Console.WriteLine($"Content filter triggered: {ex.Message}");
}

// Cleanup
await aiProjectClient.Agents.DeleteAgentAsync(agent.Name);
Console.WriteLine();
Console.WriteLine("Agent deleted successfully.");
