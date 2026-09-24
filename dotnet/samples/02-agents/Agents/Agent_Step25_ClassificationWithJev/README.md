# Classification with Jev

This sample gives an agent a classifier tool built with `JevAIToolBuilder` from `Microsoft.Agents.AI.TypeSafe`.

Jev, the TypeSafe System One model, does not write text. It answers typed questions about a state and returns
calibrated probabilities. An agent's model writes the replies; Jev makes the fast, structured decisions:

1. The agent's model calls `evaluate_with_jev` with a Jev request: the `state` to evaluate and the `questions` to
   answer about it.
2. Jev answers each question:

   | Question type | What it answers | Answer |
   | --- | --- | --- |
   | `choice` | Which of these options? | The chosen option, the probability of each option, and a confidence |
   | `score` | Which level of an ordered scale (2 to 10 levels)? | A score that can fall between levels, the probability of each level, and a confidence |
   | `noul` | Is this yes or no statement true? | The probability, from 0 to 1, that it is true |

3. The model reads the answers and writes the reply. Here it routes each support ticket and sends uncertain tickets to
   human review.

## Configuring the client

`JevAIToolBuilder` hides the HTTP client. The API key is an `ApiKeyCredential`, and `JevAIToolOptions` follows the
`ClientPipelineOptions` pattern of the OpenAI and Azure AI client libraries:

```csharp
var options = new JevAIToolOptions
{
    ModelId = "jev-latest",                                        // default
    Transport = new HttpClientPipelineTransport(httpClient),       // for example from IHttpClientFactory
    NetworkTimeout = TimeSpan.FromSeconds(30),
};
options.AddPolicy(myPolicy, PipelinePosition.PerCall);            // custom pipeline policies

AIFunction jev = new JevAIToolBuilder(new ApiKeyCredential(apiKey), options).Build();
```

The default retry policy retries rate-limited (429) and overloaded (529) requests, and fails at once when the API asks
for a wait longer than one minute, so the agent run is never blocked for long. `Name` and `Description` set what the
agent's model sees.

## Using another client

Any other client can answer the requests instead of the TypeSafe API. Pass a delegate that takes a `JevRequest` and
returns a `JevResponse`, or map between the Jev types and the client's own types:

```csharp
AIFunction jev = new JevAIToolBuilder()
    .UseClient<MyClientRequest, MyClientResponse>(
        myClient.EvaluateAsync,
        inputMapper: request => ToMyClientRequest(request),
        outputMapper: response => ToJevResponse(response))
    .Build();
```

The tool checks the mapped response before the model sees it: every question needs an answer of the same type, and a
`choice` answer and its probabilities may only use the options the question offered.

## Dependency injection

Every evaluator receives the `IServiceProvider` of the agent that calls the tool. `FunctionInvokingChatClient` passes
it in `AIFunctionArguments.Services`, from the services the agent was created with. `UseClient<TClient, ...>` resolves
a registered client from a new scope on every call and disposes the scope afterwards, so scoped and disposable clients
work even though hosted agents are singletons that receive the root provider:

```csharp
builder.Services.AddScoped<MyJevClient>();
builder.Services.AddAIAgent("triage", Instructions)
    .WithAITool(sp => new JevAIToolBuilder()
        .UseClient<MyJevClient, MyClientRequest, MyClientResponse>(
            (client, request, cancellationToken) => client.EvaluateAsync(request, cancellationToken),
            inputMapper: ToMyClientRequest,
            outputMapper: ToJevResponse)
        .Build());
```

With the TypeSafe API, build the tool in the same `WithAITool` factory and take the credential and the transport from
the container or configuration.

## Prerequisites

- .NET 10 SDK or later
- A Microsoft Foundry project with a model deployment
- Azure CLI installed and authenticated (for Azure credential authentication)
- A TypeSafe API key from the [TypeSafe console](https://console.typesafe.ai/keys)

Set the following environment variables:

```powershell
$env:FOUNDRY_PROJECT_ENDPOINT="https://your-foundry-service.services.ai.azure.com/api/projects/your-foundry-project" # Replace with your Foundry project endpoint
$env:FOUNDRY_MODEL="gpt-5.4-mini"  # Optional, defaults to gpt-5.4-mini
$env:TYPESAFE_API_KEY="<your-api-key>"
```

## Run the sample

```powershell
cd dotnet/samples/02-agents/Agents/Agent_Step25_ClassificationWithJev
dotnet run
```
