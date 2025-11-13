// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to use Code Interpreter Tool with AI Agents.

using Azure.AI.Agents;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAICUA001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

string endpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_ENDPOINT") ?? throw new InvalidOperationException("AZURE_FOUNDRY_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

const string AgentInstructions = """
    You are a computer automation assistant.

    Be direct and efficient. When you reach the search results page, read and describe the actual search result titles and descriptions you can see.
    """;

const string AgentNameMEAI = "CoderAgent-MEAI";
const string AgentNameNative = "CoderAgent-NATIVE";

Dictionary<string, DataContent> screenshots = new() {
    {"browser_search", new DataContent(File.ReadAllBytes("Assets/cua_browser_search.png"), "image/png") { AdditionalProperties = new AdditionalPropertiesDictionary { ["detail"] = ResponseImageDetailLevel.High } } },
    {"search_typed", new DataContent(File.ReadAllBytes("Assets/cua_search_typed.png"), "image/png")},
    {"search_results", new DataContent(File.ReadAllBytes("Assets/cua_search_results.png"), "image/png")},
};

// Get a client to create/retrieve/delete server side agents with Azure Foundry Agents.
AgentClient agentClient = new(new Uri(endpoint), new AzureCliCredential());

// Option 1 - Using HostedCodeInterpreterTool + AgentOptions (MEAI + AgentFramework)
// Create the server side agent version
AIAgent agentOption1 = await agentClient.CreateAIAgentAsync(
    model: deploymentName,
    options: new ChatClientAgentOptions()
    {
        Name = AgentNameMEAI,
        Instructions = AgentInstructions,
        ChatOptions = new()
        {
            Tools = [ResponseTool.CreateComputerTool(
                        environment: new ComputerToolEnvironment("windows"),
                        displayWidth: 1026,
                        displayHeight: 769).AsAITool()]
        }
    });

// Option 2 - Using PromptAgentDefinition SDK native type
// Create the server side agent version
AIAgent agentOption2 = await agentClient.CreateAIAgentAsync(
    name: AgentNameNative,
    creationOptions: new AgentVersionCreationOptions(
        new PromptAgentDefinition(model: deploymentName)
        {
            Instructions = AgentInstructions,
            Tools = { ResponseTool.CreateComputerTool(
                environment: new ComputerToolEnvironment("windows"),
                displayWidth: 1026,
                displayHeight: 769) }
        })
);

List<ChatMessage> messages =
    [
        new ChatMessage(ChatRole.User, [
            new TextContent("I need you to help me search for 'OpenAI news'. Please type 'OpenAI news' and submit the search. Once you see search results, the task is complete."),
            screenshots["browser_search"],
        ]),
    ];

// Cleanup by agent name removes the agent version created.
await agentClient.DeleteAgentAsync(agentOption1.Name);
await agentClient.DeleteAgentAsync(agentOption2.Name);
