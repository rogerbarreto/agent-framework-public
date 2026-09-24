// Copyright (c) Microsoft. All rights reserved.

// Classification with Jev — Give an agent a fast, calibrated classifier as a tool
//
// Jev, the TypeSafe System One model, does not write text: it answers typed questions
// (choice, score, noul) about a state and returns calibrated probabilities. This sample gives
// a support agent a Jev tool built with JevAIToolBuilder. The agent's model decides which
// questions to ask, Jev answers them, and the model writes the routing decision, sending
// uncertain tickets to a human.

using System.ClientModel;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.TypeSafe;
using Microsoft.Extensions.AI;

var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT") ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("FOUNDRY_MODEL") ?? "gpt-5.4-mini";
var typeSafeApiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY") ?? throw new InvalidOperationException("TYPESAFE_API_KEY is not set.");

// <create_jev_tool>
// The tool's arguments are a Jev request (the state plus typed questions) and its result is the Jev response.
AIFunction jev = new JevAIToolBuilder(new ApiKeyCredential(typeSafeApiKey)).Build();
// </create_jev_tool>

const string Instructions = """
    You triage customer support tickets. For every ticket, call evaluate_with_jev once with the ticket text as the
    state and these questions:
    - "department": a choice between billing (payments, invoices, refunds), technical (bugs, outages, integrations),
      and sales (pricing, upgrades, new accounts).
    - "urgent": a noul asking whether the customer needs help urgently.
    - "frustration": a score with the levels calm, annoyed, and angry.
    Then reply in one line with the department, whether it is urgent, and the frustration level. When the department
    confidence is below 0.6, say the ticket needs human review instead of naming a department.
    """;

// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
// latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
AIAgent agent = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
    .AsAIAgent(model: deploymentName, instructions: Instructions, name: "TicketTriage", tools: [jev]);

string[] tickets =
[
    "I've been trying to connect my Stripe account for 3 days and the integration keeps failing. I'm losing sales. Please help ASAP.",
    "Hi, could you send me a copy of last month's invoice when you get a chance? Thanks!",
    "I'd like to talk to someone about my account.",
];

foreach (string ticket in tickets)
{
    AgentResponse response = await agent.RunAsync(ticket);

    Console.WriteLine($"Ticket: {ticket}");
    foreach (FunctionResultContent result in response.Messages.SelectMany(static m => m.Contents).OfType<FunctionResultContent>())
    {
        Console.WriteLine($"Jev answers: {result.Result}");
    }

    Console.WriteLine($"Agent: {response.Text}");
    Console.WriteLine();
}
