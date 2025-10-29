// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to create and use a simple AI agent with Azure Foundry Agents as the backend.

using System.ClientModel.Primitives;
using Azure.AI.Agents;
using Azure.Identity;
using Microsoft.Agents.AI;

var endpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_ENDPOINT") ?? throw new InvalidOperationException("AZURE_FOUNDRY_PROJECT_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

const string JokerInstructions = "You are good at telling jokes.";

#pragma warning disable CA5399

using var myHttpHandler = new MyHttpHandler();
using var httpClient = new HttpClient(myHttpHandler);

// Get a client to create/retrieve server side agents with.
var agentsClient = new AgentsClient(new Uri(endpoint), new AzureCliCredential(), new() { Transport = new HttpClientPipelineTransport(httpClient) });

// Define the agent you want to create.
var agentDefinition = new PromptAgentDefinition(model: deploymentName) { Instructions = JokerInstructions };

// You can create a server side agent with the Azure.AI.Agents SDK.
var agentRecord = agentsClient.CreateAgentVersion(agentName: "Joker1", definition: agentDefinition).Value;

// You can retrieve an already created server side agent as an AIAgent.
AIAgent existingAgent = await agentsClient.GetAIAgentAsync(deploymentName, agentRecord.Name, openAIClientOptions: new() { Transport = new HttpClientPipelineTransport(httpClient) });

// You can also create a server side persistent agent and return it as an AIAgent directly.
//var createdAgent = agentsClient.CreateAIAgent(deploymentName, name: "Joker2", instructions: JokerInstructions);

// You can then invoke the agent like any other AIAgent.
AgentThread thread = existingAgent.GetNewThread();
Console.WriteLine(await existingAgent.RunAsync("Tell me a joke about a pirate.", thread));

// Cleanup for sample purposes.
agentsClient.DeleteAgent(agentRecord.Name);
//agentsClient.DeleteAgent(createdAgent.Name);

internal sealed class MyHttpHandler : HttpClientHandler
{
    protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var result = await base.SendAsync(request, cancellationToken);

        if (result.StatusCode >= System.Net.HttpStatusCode.BadRequest)
        {
            if (request.Content is not null)
            {
                var requestUri = request.RequestUri?.ToString();
                var requestBody = await request.Content.ReadAsStringAsync(cancellationToken);

                Console.WriteLine($"Request URI: {requestUri}");
                Console.WriteLine($"Request Body: {requestBody}");
            }

            var responseBody = await result.Content.ReadAsStringAsync(cancellationToken);

            Console.WriteLine($"Response Body: {responseBody}");
        }

        return result;
    }
}
