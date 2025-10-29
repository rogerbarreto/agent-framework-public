// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to create and use a simple AI agent with Azure Foundry Agents as the backend.

using Azure.AI.Agents;
using Azure.Identity;
using Microsoft.Agents.AI;

var endpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_ENDPOINT") ?? throw new InvalidOperationException("AZURE_FOUNDRY_PROJECT_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

const string JokerName = "Joker";
const string JokerInstructions = "You are good at telling jokes.";

// Get a client to create/retrieve server side agents with.
var agentsClient = new AgentsClient(new Uri(endpoint), new AzureCliCredential());

// Define the agent you want to create.
var agentDefinition = new PromptAgentDefinition(model: deploymentName) { Instructions = JokerInstructions };

// You can create a server side agent with the Azure.AI.Agents SDK.
var agentRecord = agentsClient.CreateAgent(name: JokerName, definition: agentDefinition).Value;

// You can retrieve an already created server side agent as an AIAgent.
AIAgent existingAgent = await agentsClient.GetAIAgentAsync(agentRecord.Name);

// You can also create a server side persistent agent and return it as an AIAgent directly.
var createdAgent = agentsClient.CreateAIAgent(deploymentName, name: JokerName, instructions: JokerInstructions);

// You can then invoke the agent like any other AIAgent.
AgentThread thread = existingAgent.GetNewThread();
Console.WriteLine(await existingAgent.RunAsync("Tell me a joke about a pirate.", thread));

// Cleanup for sample purposes.
agentsClient.DeleteAgent(agentRecord.Name);
agentsClient.DeleteAgent(createdAgent.Name);
