// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to create and use a AI agents with Azure Foundry Agents as the backend.

using Anthropic.Client;
using Anthropic.Client.Core;
using Microsoft.Agents.AI;

var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not set.");
var model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-haiku-4-5";

const string JokerInstructions = "You are good at telling jokes.";
const string JokerName = "JokerAgent";

var client = new AnthropicClient(new ClientOptions { APIKey = apiKey });

AIAgent agent = client.CreateAIAgent(model: model, instructions: JokerInstructions, name: JokerName);

// Invoke the agent and output the text result.
// Console.WriteLine(await agent.RunAsync("Tell me a joke about a pirate."));
