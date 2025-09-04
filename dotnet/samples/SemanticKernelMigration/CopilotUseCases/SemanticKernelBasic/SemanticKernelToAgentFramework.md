# Instructions for migrating from Semantic Kernel Agents to Agent Framework in .NET projects.

## Scope

When you are asked to migrate a project from `Microsoft.SemanticKernel.Agents` to `Microsoft.Extensions.AI.Agents` you need to determine for which projects you need to do it.
If a single project is specified - do it for that project only. If you are asked to do it for a solution, migrate all projects in the solution
that reference `Microsoft.SemanticKernel.Agents` or related Semantic Kernel agent packages. If you don't know which projects to migrate, ask the user.

## Things to consider while doing migration

- NuGet package names, assembly names, projects names or other dependencies names are case insensitive(!). You ***must take it into account*** when doing something
  with project dependencies, like searching for dependencies or when removing them from projects etc.
- Agent Framework uses different namespace patterns and API structures compared to Semantic Kernel Agents
- Tool registration patterns are significantly simplified in Agent Framework
- Thread management is handled differently between the two frameworks
- Dependency injection patterns have been streamlined in Agent Framework
- Text-based heuristics should be avoided in favor of proper content type inspection when available.

## Planning

For each project that needs to be migrated, you need to do the following:

- Find projects depending on `Microsoft.SemanticKernel.Agents` or related Semantic Kernel agent packages (when searching for projects, if some projects are not part of the
  solution or you could not find the project, notify user and continue with other projects).
- Identify the specific Semantic Kernel agent types being used (ChatCompletionAgent, OpenAIAssistantAgent, AzureAIAgent, etc.)
- Determine the AI provider being used (OpenAI, Azure OpenAI, Azure AI Foundry, etc.)
- Analyze tool/function registration patterns
- Review thread management and invocation patterns

## Execution

***Important***: when running steps in this section you must not pause, you must continue until you are done with all steps or you are truly unable to
continue and need user's interaction (you will be penalized if you stop unnecessarily).

Keep in mind information in the next section about differences and follow these steps in the order they are specified (you will be penalized if you do steps
below in wrong order or skip any of them):

1. For each project that has an explicit package dependency to Semantic Kernel agent packages in the project file or some imported MSBuild targets (some
   project could receive package dependencies transitively, so avoid adding new package dependencies for such projects), do the following:

- Remove the Semantic Kernel agent package references from the project file:
  - `Microsoft.SemanticKernel.Agents.Core`
  - `Microsoft.SemanticKernel.Agents.OpenAI`
  - `Microsoft.SemanticKernel.Agents.AzureAI`
  - `Microsoft.SemanticKernel` (if only used for agents)
- Add the appropriate Agent Framework package references based on the provider being used:
  - `Microsoft.Extensions.AI.Agents.Abstractions` (always required)
  - `Microsoft.Extensions.AI.Agents.OpenAI` (for OpenAI and Azure OpenAI providers)
  - For unsupported providers (Bedrock, CopilotStudio), note in the report that custom implementation is required
- If projects use Central Package Management, update the `Directory.Packages.props` file to remove the Semantic Kernel agent package versions in addition to
  removing package reference from projects.
  When adding the Agent Framework PackageReferences, add them to affected project files without a version and add PackageVersion elements to the
  Directory.Packages.props file with the version that supports the project's target framework.

2. Update code files using Semantic Kernel Agents in the selected projects (and in projects that depend on them since they could receive Semantic Kernel transitively):

- Find ***all*** code files in the selected projects (and in projects that depend on them since they could receive Semantic Kernel transitively).
  When doing search of code files that need changes, prefer calling search tools with `upgrade_` prefix if available. Also do pass project's root folder for all
  selected projects or projects that depend on them.
- Update the code files that use Semantic Kernel Agents to use Agent Framework instead. You never should add placeholders when updating code, or remove any comments in the code files,
  you must keep the business logic as close as possible to the original code but use new API. When checking if code file needs to be updated, you should check for
  using statements, types and API from `Microsoft.SemanticKernel.Agents` namespace (skip comments and string literal constants).
- Ensure that you replace all Semantic Kernel agent using statements with Agent Framework using statements (always check if there are any other Semantic Kernel agent
  API used in the file having any of the Semantic Kernel agent using statements; if no other API detected, Semantic Kernel agent using statements should be just removed
  instead of replaced). If there were no Semantic Kernel agent using statements in the file, do not add Agent Framework using statements.
- When replacing types you must ensure that you add using statements for them, since some types that lived in main `Microsoft.SemanticKernel.Agents` namespace live in other namespaces
  under `Microsoft.Extensions.AI.Agents`. For example, `Microsoft.SemanticKernel.Agents.ChatCompletionAgent` is replaced with `Microsoft.Extensions.AI.Agents.ChatClientAgent`, when that
  happens using statement with `Microsoft.Extensions.AI.Agents` needs to be added (unless you use fully qualified type name)
- If you see some code that really cannot be converted or will have potential behavior changes at runtime, remember files and code lines where it
  happens at the end of the migration process you will generate a report markdown file and list all follow up steps user would have to do.

3. Validate that all places where Semantic Kernel Agents were used are migrated. To do that search for `Microsoft.SemanticKernel.Agents` in all affected projects and projects that depend
   on them again and if still see any Semantic Kernel agent presence go back to step 2. Steps 2 and 3 should be repeated until you see no Semantic Kernel agent references.

4. Build all modified projects to ensure that they compile without errors. If there are any build errors, you must fix them all yourself one by one and
   don't stop until all errors are fixed.

5. Generate the report file under `<solution root>\.github folder`, the file name should be `SemanticKernelToAgentFrameworkReport.md`, it is highly important that
   you generate report when migration complete. Report should contain:
     - all project dependencies changes (mention what was changed, added or removed, including provider-specific packages)
     - all code files that were changed (mention what was changed in the file, if it was not changed, just mention that the file was not changed)
     - provider-specific migration patterns used (OpenAI, Azure OpenAI, Azure AI Foundry, A2A, ONNX, etc.)
     - all cases where you could not convert the code because of unsupported features and you were unable to find a workaround
     - unsupported providers that require custom implementation (Bedrock, CopilotStudio)
     - breaking glass pattern migrations (InnerContent → RawRepresentation) and any CodeInterpreter or advanced tool usage
     - all behavioral changes that have to be verified at runtime
     - provider-specific configuration changes that may affect behavior
     - all follow up steps that user would have to do in the report markdown file

## Detailed information about differences in Semantic Kernel Agents and Agent Framework

The Agent Framework provides functionality for creating and managing AI agents with simplified APIs and better performance. The Agent Framework is included in the Microsoft.Extensions.AI package ecosystem and provides a unified interface across different AI providers.

Agent Framework focuses primarily on simplicity, performance, and developer experience. It has some key differences in default behavior and doesn't aim to have complete feature parity with Semantic Kernel Agents. For some scenarios, Agent Framework currently has different patterns, but there are recommended migration paths. For other scenarios, the migration provides significant improvements in code clarity and maintainability.

The Agent Framework team is investing in adding the features that are most often requested. If your application depends on a missing feature, consider filing an issue in the dotnet/extensions GitHub repository to find out if support for your scenario can be added.

Most of this article is about how to use the AIAgent API, but it also includes guidance on how to use the AgentThread, AIFunction, and other related types.

### Table of differences

The following table lists Semantic Kernel Agents features and Agent Framework equivalents. The equivalents fall into the following categories and should be used as guidance for migration:

| Semantic Kernel Agents feature                              | Agent Framework equivalent                                   |
|--------------------------------------------------------------|--------------------------------------------------------------|
| Microsoft.SemanticKernel.Agents namespace                   | Microsoft.Extensions.AI.Agents namespace                    |
| ChatCompletionAgent class                                   | ChatClientAgent class                                        |
| OpenAIAssistantAgent class                                  | Extension to get a ChatClientAgent from the OpenAI Assistants client |
| AzureAIAgent class                                          | Extension to get a ChatClientAgent from the Azure AI Foundry client  |
| OpenAI Response agents                                      | Extension to get a ChatClientAgent from the OpenAI Responses client  |
| A2A agents                                                  | A2ACardResolver.GetAIAgent() with A2A provider              |
| AIAgent interface (abstract)                                | AIAgent abstract class (for retrieval/agnostic code only)   |
| Kernel-based agent creation                                 | Direct agent creation                                       |     
| KernelFunction and KernelPlugin for function tools          | AIFunction for function tools                               |
| Manual thread creation with specific types                  | agent.GetNewThread() for thread creation                    |
| InvokeAsync method                                          | RunAsync method                                             |
| InvokeStreamingAsync method                                 | RunStreamingAsync method                                    |
| AgentInvokeOptions for configuration                        | Provider-specific run options (e.g., ChatClientAgentRunOptions) |
| IAsyncEnumerable<AgentResponseItem<ChatMessageContent>> for non-streaming     | AgentRunResponse for non-streaming        |
| IAsyncEnumerable<StreamingChatMessageContent> for streaming | IAsyncEnumerable<AgentRunResponseUpdate> for streaming      |
| KernelArguments for execution settings                      | Direct run options configuration                            |
| [KernelFunction] attribute required for function tools      |                                                             |
| Plugin-based tool registration                              | Direct function registration                                |
| AgentThread.DeleteAsync() for cleanup                       | Provider-client-specific thread cleanup                     |
| `InnerContent` property for breaking glass                  | `RawRepresentation` property for breaking glass             |
| Non-streaming has no abstraction for Usage metadata         | Non-streaming supports Usage via `response.Usage`                                |
| Streaming has no abstraction for Usage metadata             | Streaming supports Usage via `update.Contents.OfType<UsageContent>()?.FirstOrDefault()?.Details` |

This is not an exhaustive list of Semantic Kernel Agents features. The list includes many of the scenarios that are commonly used in agent applications.

### Differences in default behavior

Agent Framework is designed for simplicity and performance by default, emphasizing clear and intuitive APIs. The framework is intentionally designed this way for better developer experience and reduced complexity. Semantic Kernel Agents provided more flexibility but with increased complexity. This fundamental difference in design is behind many of the following specific differences in default behavior.

### Namespace Updates

During migration, Semantic Kernel Agents namespaces need to be updated to Agent Framework namespaces. The Agent Framework uses `Microsoft.Extensions.AI` as the root namespace for all AI-related abstraction types.

**Semantic Kernel Agents namespaces:**
```csharp
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Connectors.OpenAI;
```

**Agent Framework namespaces:**
```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Agents;
```

### Agent Creation Simplification

**Semantic Kernel Agents** 

`ChatCompletionAgents` requires an instance of the `Kernel` to be provided during creation:

```csharp
Kernel kernel = Kernel.CreateBuilder()
    .AddOpenAIChatClient(modelId, apiKey)
    .Build();

ChatCompletionAgent agent = new()
{
    Instructions = "You are a helpful assistant",
    Kernel = kernel
};
```

**Agent Framework ChatClientAgent** 

`ChatClientAgents` only requires the `IChatClient` to be provided during creation.

```csharp
// ChatClientAgent direct constructor creation
using var chatClient = new OpenAIClient(apiKey).GetChatClient(modelId).AsIChatClient();
AIAgent agent = new ChatClientAgent(chatClient, instructions: "You are a helpful assistant");

// OR 

// Simplified experience via extension for the selected provider.
AIAgent agent = new OpenAIClient(apiKey)
    .GetChatClient(modelId)
    .CreateAIAgent(instructions: "You are a helpful assistant");
```

### Thread Management Changes

**Semantic Kernel Agents** 

Requires the caller to know what is the thread type to create it manually.

```csharp
// Different thread types for different scenarios
AgentThread thread = new ChatHistoryAgentThread();
AgentThread thread = new OpenAIAssistantAgentThread(assistantClient);
AgentThread thread = new AzureAIAgentThread(azureClient);
```

**Agent Framework** 

Provides unified thread creation through the agent:

```csharp
// Unified GetNewThread() on all agents
AgentThread thread = chatClientAgent.GetNewThread();
AgentThread thread = assistantAgent.GetNewThread();
AgentThread thread = azureAIAgent.GetNewThread();
```

### Tool Registration Differences

**Semantic Kernel Agents** 

Requires a `KernelPlugin` setup where you normally need to provide a `class` or an `instance` where it must have at least one method decorated with `[KernelFunction]` attributes.

```csharp
[KernelFunction]
[Description("Get the weather for a location")]
static string GetWeather(string location) => $"Weather in {location}";

KernelFunction kernelFunction = KernelFunctionFactory.CreateFromMethod(GetWeather);
KernelPlugin kernelPlugin = KernelPluginFactory.CreateFromFunctions("WeatherPlugin", [kernelFunction]);
kernel.Plugins.Add(kernelPlugin);

ChatCompletionAgent agent = new() { Kernel = kernel };
```

**Agent Framework** 

Allows direct function method registration without need for decorators

```csharp
[Description("Get the weather for a location")]
static string GetWeather(string location) => $"Weather in {location}";

AIAgent agent = chatClient.CreateAIAgent(
    tools: [AIFunctionFactory.Create(GetWeather)]);
```

### Invocation Method Changes

**Semantic Kernel Agents** 

Non-streaming invocation: `InvokeAsync` uses a streaming IAsyncEnumerable pattern for returning multiple agent messages.

```csharp
// Non-streaming
await foreach (AgentResponseItem<ChatMessageContent> item in agent.InvokeAsync(userInput, thread, options))
{
    Console.WriteLine(item.Message);
}
```

Streaming invocation  `InvokeStreamingAsync` instead of full messages return updates as they are produced in the streaming IAsyncEnumerable pattern. 

```csharp
// Streaming
await foreach (StreamingChatMessageContent update in agent.InvokeStreamingAsync(userInput, thread, options))
{
    Console.Write(update.Message);
}
```

**Agent Framework** 

Non-streaming invocation: `RunAsync` returns a single `AgentRunResponse` with the agent response that can contain multiple messages.

```csharp
// Non-streaming
AgentRunResponse result = await agent.RunAsync(userInput, thread, options);
Console.WriteLine(result);
```

Streaming invocation: `RunStreamingAsync` returns an `IAsyncEnumerable<AgentRunResponseUpdate>` with the agent updates as they are produced.

```csharp
// Streaming
await foreach (var update in agent.RunStreamingAsync(userInput, thread, options))
{
    Console.Write(update);
}
```

### Options and Configuration Changes

**Semantic Kernel Agents** used complex nested options:

```csharp
OpenAIPromptExecutionSettings settings = new() { MaxTokens = 1000 };
AgentInvokeOptions options = new() { KernelArguments = new(settings) };
```

**Agent Framework** uses simplified provider-specific options:

```csharp
ChatClientAgentRunOptions options = new(new() { MaxOutputTokens = 1000 });
```

### Dependency Injection Simplification

**Semantic Kernel Agents** required Kernel registration:

```csharp
services.AddKernel().AddOpenAIChatClient(modelId, apiKey);
services.AddTransient<ChatCompletionAgent>(sp => new()
{
    Kernel = sp.GetRequiredService<Kernel>(),
    Instructions = "You are helpful"
});
```

**Agent Framework** allows direct agent registration:

```csharp
services.AddTransient<AIAgent>(sp =>
    new OpenAIClient(apiKey)
        .GetChatClient(modelId)
        .CreateAIAgent(instructions: "You are helpful"));
```

### Thread Cleanup Changes

**Semantic Kernel Agents** provided thread deletion methods:

```csharp
await thread.DeleteAsync(); // For hosted threads
```

**Agent Framework** delegates cleanup to the provider when needed:

```csharp
// For providers that require cleanup (like OpenAI Assistants)
await assistantClient.DeleteThreadAsync(thread.ConversationId);
```

### Provider-Specific Implementations

Agent Framework provides different implementations for different AI providers:

**OpenAI Chat Completion:**
```csharp
AIAgent agent = new OpenAIClient(apiKey)
    .GetChatClient(modelId)
    .CreateAIAgent(instructions: instructions);
```

**OpenAI Assistants:**
```csharp
AIAgent agent = new OpenAIClient(apiKey)
    .GetAssistantClient()
    .CreateAIAgent(instructions: instructions);
```

**Azure OpenAI:**
```csharp
AIAgent agent = new AzureOpenAIClient(endpoint, credential)
    .GetChatClient(deploymentName)
    .CreateAIAgent(instructions: instructions);
```

**Azure AI Foundry:**
```csharp
AIAgent agent = new PersistentAgentsClient(endpoint, credential)
    .CreateAIAgent(instructions: instructions);
```

### Migration Scenarios

#### Basic Agent Creation
**Before (Semantic Kernel):**
```csharp
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

Kernel kernel = Kernel.CreateBuilder()
    .AddOpenAIChatClient(modelId, apiKey)
    .Build();

ChatCompletionAgent agent = new()
{
    Instructions = "You are helpful",
    Kernel = kernel
};

AgentThread thread = new ChatHistoryAgentThread();
```

**After (Agent Framework):**
```csharp
using Microsoft.Extensions.AI.Agents;
using OpenAI;

AIAgent agent = new OpenAIClient(apiKey)
    .GetChatClient(modelId)
    .CreateAIAgent(instructions: "You are helpful");

AgentThread thread = agent.GetNewThread();
```

#### Tool Registration
**Before (Semantic Kernel):**
```csharp
[KernelFunction]
[Description("Get weather information")]
static string GetWeather([Description("Location")] string location)
    => $"Weather in {location}";

KernelFunction function = KernelFunctionFactory.CreateFromMethod(GetWeather);
KernelPlugin plugin = KernelPluginFactory.CreateFromFunctions("Weather", [function]);
kernel.Plugins.Add(plugin);
```

**After (Agent Framework):**
```csharp
[Description("Get weather information")]
static string GetWeather([Description("Location")] string location)
    => $"Weather in {location}";

AIAgent agent = chatClient.CreateAIAgent(
    tools: [AIFunctionFactory.Create(GetWeather)]);
```

#### Agent Invocation
**Before (Semantic Kernel):**
```csharp
OpenAIPromptExecutionSettings settings = new() { MaxTokens = 1000 };
AgentInvokeOptions options = new() { KernelArguments = new(settings) };

await foreach (var result in agent.InvokeAsync(input, thread, options))
{
    Console.WriteLine(result.Message);
}
```

**After (Agent Framework):**
```csharp
ChatClientAgentRunOptions options = new(new() { MaxOutputTokens = 1000 });

AgentRunResponse result = await agent.RunAsync(input, thread, options);
Console.WriteLine(result);

// Breaking glass to access underlying content if needed
var chatResponse = result.RawRepresentation as ChatResponse;
// Access underlying SDK objects via chatResponse?.RawRepresentation
```

### Usage Metadata

#### Semantc Kernel Pattern

Semantic Kernel Agents don't provide an abstraction for usage metadata to get the token usage details you need to use the metadata dictionary combined with a breaking glass casting to a provider-specific Usage type.

Non-Streaming:
```csharp
await foreach (var result in agent.InvokeAsync(input, thread, options))
{
    if (result.Message.Metadata?.TryGetValue("Usage", out object? usage) ?? false)
    {
        if (usage is ChatTokenUsage openAIUsage)
        {
            Console.WriteLine($"Tokens: {openAIUsage.TotalTokenCount}");
        }
    }
}
```

Streaming:
```csharp
 await foreach (StreamingChatMessageContent response in agent.InvokeStreamingAsync(message, agentThread))
 {
    if (response.Metadata?.TryGetValue("Usage", out object? usage) ?? false)
    {
        if (usage is ChatTokenUsage openAIUsage)
        {
            Console.WriteLine($"Tokens: {openAIUsage.TotalTokenCount}");
        }
    }
 }
```

#### Agent Framework Pattern

Agent Framework provides a unified `UsageDetails` type for accessing usage metadata.

Non-Streaming:
```csharp
AgentRunResponse result = await agent.RunAsync(input, thread, options);
Console.WriteLine($"Tokens: {result.Usage.TotalTokenCount}");
```

Streaming:
```csharp
await foreach (AgentRunResponseUpdate update in agent.RunStreamingAsync(input, thread, options))
{
    if (update.Contents.OfType<UsageContent>().FirstOrDefault() is { } usageContent)
    {
        Console.WriteLine($"Tokens: {usageContent.Details.TotalTokenCount}");
    }
}
```

### Breaking Glass Strategy for Accessing Underlying Content

One of the most important migration patterns involves accessing underlying SDK objects and content. This is crucial for scenarios like CodeInterpreter tools, reasoning models, and other advanced features.

#### Semantic Kernel Pattern (InnerContent)

**Semantic Kernel** used `InnerContent` to access underlying SDK objects:

```csharp
// SK: Accessing underlying content via InnerContent
await foreach (var content in agent.InvokeAsync(userInput, thread))
{
    if (content.Message.InnerContent is OpenAI.Chat.ChatMessage underlyingChatMessage)
    {
        // Access underlying SDK objects via chatResponse
    }
}
```

#### Agent Framework Pattern (RawRepresentation)

**Agent Framework** uses `RawRepresentation` 

When the agent is a `ChatClientAgent`, to access the underlying SDK a double-breaking glass pattern is required where:
the first breaking glass exposes the `Microsoft.Extensions.AI` type, and the second breaking glass exposes the underlying SDK type.

Below is an example how to retrieve different `RunStepDetailsUpdate` updates from the `AgentRunResponse` raw representation and check for the `CodeInterpreterInput` property to capture the generated code.

```csharp
// AF: Breaking glass to access underlying SDK objects
var result = await agent.RunAsync(userInput, thread);

// First level: AgentRunResponse.RawRepresentation exposes ME.AI type (e.g., ChatResponse)
var chatResponse = result.RawRepresentation as ChatResponse;

// Second level: ME.AI type.RawRepresentation exposes underlying SDK type
foreach (object? updateRawRepresentation in chatResponse?.RawRepresentation as IEnumerable<object?> ?? [])
{
    if (updateRawRepresentation is RunStepDetailsUpdate update && update.CodeInterpreterInput is not null)
    {
        generatedCode.Append(update.CodeInterpreterInput);
    }
}
```

#### CodeInterpreter Tool Migration 

Non-Streaming Semantic Kernel example of accessing CodeInterpreter tool output:

**Before (Semantic Kernel):**
```csharp
await foreach (var content in agent.InvokeAsync(userInput, thread))
{
    bool isCode = content.Message.Metadata?.ContainsKey(AzureAIAgent.CodeInterpreterMetadataKey) ?? false;
    Console.WriteLine($"# {content.Message.Role}{(isCode ? "\n# Generated Code:\n" : ":")}{content.Message.Content}");

    // Process annotations
    foreach (var item in content.Message.Items)
    {
        if (item is AnnotationContent annotation)
        {
            Console.WriteLine($"[{item.GetType().Name}] {annotation.Label}: File #{annotation.ReferenceId}");
        }
        else if (item is FileReferenceContent fileReference)
        {
            Console.WriteLine($"[{item.GetType().Name}] File #{fileReference.FileId}");
        }
    }
}
```

**After (Agent Framework):**

The changes compared to the Semantic Kernel are significant and may vary depending on the provider. 

1.	Code interpreter output is its own separate thing, not a property or characteristic of a TextContent
2.	We need to directly access and handle the code output from the raw representation
3.	The TextContent and CodeInterpreter output are separate things to process independently
4. In the Azure AI Foundry non-streaming case the raw representation `object?` from the `AgentRunResponse` can be casted to a list of the `RunStepDetailsUpdate` type which can be checked for the `CodeInterpreterInput` property to capture the generated code.
5. For a detailed Non-Streaming Agent Framework Azure AI Foundry example see: `dotnet\samples\SemanticKernelMigration\AzureAIFoundry\Step04_CodeInterpreter\Program.cs`.

#### ChatOptions Custom Model Settings

When using `ChatClientAgent` with `ChatClientAgentRunOptions`, there may be cases where the `ChatOptions` properties may not have an equivalent abstraction for the desired model setting (e.g. `reasoning effort level` for reasoning models).

For advanced scenarios like this, you can use the breaking-glass `RawRepresentationFactory` property of the `ChatOptions` to instruct the agent how to configure the provider-specific configuration. In the sample below we are using the OpenAI SDK specific `ResponseCreationOptions` type to configure the reasoning effort level.

```csharp
var agentOptions = new ChatClientAgentRunOptions(new()
{
    MaxOutputTokens = 8000,
    // Breaking glass to access provider-specific options
    RawRepresentationFactory = (_) => new OpenAI.Responses.ResponseCreationOptions()
    {
        ReasoningOptions = new()
        {
            ReasoningEffortLevel = OpenAI.Responses.ResponseReasoningEffortLevel.High,
            ReasoningSummaryVerbosity = OpenAI.Responses.ResponseReasoningSummaryVerbosity.Detailed
        }
    }
});
```

#### Extension Methods for Type-Safe Breaking Glass

Agent Framework provides extension methods for safer access to underlying types:

```csharp
using OpenAI; // Brings in extension methods

// Type-safe extraction of OpenAI ChatCompletion
var chatCompletion = result.AsChatCompletion();

// Access underlying OpenAI objects safely
var openAIResponse = chatCompletion.GetRawResponse();
```

### Performance and Memory Improvements

Agent Framework provides several performance improvements:

- **Reduced Object Allocation**: Simplified object creation patterns
- **Better Memory Usage**: Streamlined thread and message management
- **Faster Startup**: Elimination of complex Kernel initialization
- **Optimized Serialization**: More efficient message serialization patterns

### Migration Validation

After migration, validate the following:

1. **Compilation**: All projects compile without errors
2. **Functionality**: Agent responses are equivalent to original behavior
3. **Tool Calls**: Functions are called correctly with proper parameters
4. **Thread Management**: Conversations maintain state properly
5. **Error Handling**: Exceptions are handled appropriately
6. **Performance**: Response times are acceptable or improved

### Common Migration Issues

#### Missing Using Statements
Ensure proper namespace imports for Agent Framework types.

#### Tool Function Signatures
Remove `[KernelFunction]` attributes.

#### Thread Type Mismatches
Replace specific thread types with unified `Microsoft.Extensions.AI.AgentThread` class.

#### Options Configuration
Update from `AgentInvokeOptions` to a `ChatClientAgentRunOptions` or a specialized provider-specific run options if available.

#### Dependency Injection
Simplify service registration by removing Kernel dependencies.

### Best Practices for Migration

1. **Incremental Migration**: Migrate one agent at a time
2. **Test Coverage**: Ensure comprehensive testing of migrated functionality
3. **Documentation**: Update code comments and documentation
4. **Error Handling**: Review and update exception handling patterns
5. **Performance Testing**: Validate performance improvements
6. **Code Review**: Have migrated code reviewed by team members

## Provider-Specific Migration Patterns

The following sections provide detailed migration patterns for each supported provider, covering package references, agent creation patterns, and provider-specific configurations.

### 1. OpenAI Chat Completion Migration

**Semantic Kernel Packages:**
```xml
<PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" />
```

**Agent Framework Packages:**
```xml
<PackageReference Include="Microsoft.Extensions.AI.Agents.OpenAI" />
```

**Before (Semantic Kernel):**
```csharp
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

Kernel kernel = Kernel.CreateBuilder()
    .AddOpenAIChatClient(modelId, apiKey)
    .Build();

ChatCompletionAgent agent = new()
{
    Instructions = "You are a helpful assistant",
    Kernel = kernel
};

AgentThread thread = new ChatHistoryAgentThread();
```

**After (Agent Framework):**
```csharp
using Microsoft.Extensions.AI.Agents;
using OpenAI;

AIAgent agent = new OpenAIClient(apiKey)
    .GetChatClient(modelId)
    .CreateAIAgent(instructions: "You are a helpful assistant");

AgentThread thread = agent.GetNewThread();
```

### 2. Azure OpenAI Chat Completion Migration

If the authentication is not using `AzureCliCredential` you can use `ApiKeyCredential` instead without the need for `Azure.Identity` package.

**Semantic Kernel Packages:**
```xml
<PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" />
<PackageReference Include="Azure.AI.OpenAI" />
<PackageReference Include="Azure.Identity" />
```

**Agent Framework Packages:**
```xml
<PackageReference Include="Microsoft.Extensions.AI.Agents.OpenAI" />
<PackageReference Include="Azure.AI.OpenAI" />
<PackageReference Include="Azure.Identity" />
```

**Before (Semantic Kernel):**
```csharp
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Azure.Identity;

Kernel kernel = Kernel.CreateBuilder()
    .AddAzureOpenAIChatClient(deploymentName, endpoint, new AzureCliCredential())
    .Build();

ChatCompletionAgent agent = new()
{
    Instructions = "You are a helpful assistant",
    Kernel = kernel
};
```

**After (Agent Framework):**
```csharp
using Microsoft.Extensions.AI.Agents;
using Azure.AI.OpenAI;
using Azure.Identity;

AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
    .GetChatClient(deploymentName)
    .CreateAIAgent(instructions: "You are a helpful assistant");
```

### 3. OpenAI Assistants Migration

**Semantic Kernel Packages:**
```xml
<PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" />
```

**Agent Framework Packages:**
```xml
<PackageReference Include="Microsoft.Extensions.AI.Agents.OpenAI" />
```

**Before (Semantic Kernel):**
```csharp
using Microsoft.SemanticKernel.Agents.OpenAI;
using OpenAI.Assistants;

AssistantClient assistantClient = new(apiKey);
Assistant assistant = await assistantClient.CreateAssistantAsync(
    modelId,
    instructions: "You are a helpful assistant");

OpenAIAssistantAgent agent = new(assistant, assistantClient)
{
    Kernel = kernel
};

AgentThread thread = new OpenAIAssistantAgentThread(assistantClient);
```

**After (Agent Framework):**
```csharp
using Microsoft.Extensions.AI.Agents;
using OpenAI;

AIAgent agent = new OpenAIClient(apiKey)
    .GetAssistantClient()
    .CreateAIAgent(modelId, instructions: "You are a helpful assistant");

AgentThread thread = agent.GetNewThread();

// Cleanup when needed
await assistantClient.DeleteThreadAsync(thread.ConversationId);
```

### 4. Azure AI Foundry (AzureAIAgent) Migration

**Semantic Kernel Packages:**
```xml
<PackageReference Include="Microsoft.SemanticKernel.Agents.AzureAI" />
<PackageReference Include="Azure.Identity" />
```

**Agent Framework Packages:**
```xml
<PackageReference Include="Microsoft.Extensions.AI.Agents.AzureAI" />
<PackageReference Include="Azure.Identity" />
```

**Before (Semantic Kernel):**
```csharp
using Microsoft.SemanticKernel.Agents.AzureAI;
using Azure.Identity;

AzureAIAgent agent = new(
    endpoint: new Uri(endpoint),
    credential: new AzureCliCredential(),
    projectId: projectId)
{
    Instructions = "You are a helpful assistant"
};

AgentThread thread = new AzureAIAgentThread(agent);
```

**After (Agent Framework):**
```csharp
using Microsoft.Extensions.AI.Agents;
using Azure.AI.Agents.Persistent;
using Azure.Identity;

var client = new PersistentAgentsClient(endpoint, new AzureCliCredential());

// Create an AIAgent using Agent Framework - pass model ID as required by the API
AIAgent agent = client.CreateAIAgent(
    model: deploymentName,
    instructions: "You are a helpful assistant",
    tools: [/* tool definitions */]);

AgentThread thread = agent.GetNewThread();
```

### 5. A2A Migration

**Semantic Kernel Packages:**
```xml
<PackageReference Include="Microsoft.SemanticKernel.Agents.A2A" />
```

**Agent Framework Packages:**
```xml
<PackageReference Include="Microsoft.Extensions.AI.Agents.A2A" />
```

**Before (Semantic Kernel):**
```csharp
// Create an A2A agent instance
using var httpClient = CreateHttpClient();
var client = new A2AClient(url, httpClient);
var cardResolver = new A2ACardResolver(url, httpClient);
var agentCard = await cardResolver.GetAgentCardAsync();
var agent = new A2AAgent(client, agentCard);
```

**After (Agent Framework):**
```csharp
// Initialize an A2ACardResolver to get an A2A agent card.
A2ACardResolver agentCardResolver = new(new Uri(a2aAgentHost));

// Create an instance of the AIAgent for an existing A2A agent specified by the agent card.
AIAgent agent = await agentCardResolver.GetAIAgentAsync();
```

### 6. OpenAI Responses Migration

**Semantic Kernel Packages:**
```xml
<PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" />
```

**Agent Framework Packages:**
```xml
<PackageReference Include="Microsoft.Extensions.AI.Agents.OpenAI" />
```

**Before (Semantic Kernel):**

The thread management is done manually with OpenAI Responses in Semantic Kernel, where the thread 
needs to be passed to the `InvokeAsync` method and updated with the `item.Thread` from the response.

```csharp
using Microsoft.SemanticKernel.Agents.OpenAI;

// Define the agent
OpenAIResponseAgent agent = new(new OpenAIClient(apiKey))
{
    Name = "ResponseAgent",
    Instructions = "Answer all queries in English and French.",
};

// Initial thread can be null as it will be automatically created
AgentThread? agentThread = null;

var responseItems = agent.InvokeAsync(new ChatMessageContent(AuthorRole.User, "Input message."), agentThread);
await foreach (AgentResponseItem<ChatMessageContent> responseItem in responseItems)
{
    // Update the thread to maintain the conversation for future interaction
    agentThread = responseItem.Thread;

    WriteAgentChatMessage(responseItem.Message);
}
```

**After (Agent Framework):**

Agent Framework automatically manages the thread, so there's no need to manually update it.

```csharp
using Microsoft.Extensions.AI.Agents.OpenAI;

AIAgent agent = new OpenAIClient(apiKey)
    .GetOpenAIResponseClient(modelId)
    .CreateAIAgent(
        name: "ResponseAgent",
        instructions: "Answer all queries in English and French.",
        tools: [/* AITools */]);

AgentThread thread = agent.GetNewThread();

var result = await agent.RunAsync(userInput, thread);

// The thread will be automatically updated with the new response id from this point
```

### 7. Azure OpenAI Responses Migration

Azure OpenAI Responses samples is almost similar to OpenAI Responses, with the single difference that the `OpenAIResponseAgent` is created with a  `AzureOpenAIClient` instead of `OpenAIClient`.

**Semantic Kernel Packages:**
```xml
<PackageReference Include="Microsoft.SemanticKernel.Agents.OpenAI" />
<PackageReference Include="Azure.AI.OpenAI" />
```

**Agent Framework Packages:**
```xml
<PackageReference Include="Microsoft.Extensions.AI.Agents.OpenAI" />
<PackageReference Include="Azure.AI.OpenAI" />
```
The thread management is done manually with OpenAI Responses in Semantic Kernel, where the thread 
needs to be passed to the `InvokeAsync` method and updated with the `item.Thread` from the response.

```csharp
using Microsoft.SemanticKernel.Agents.OpenAI;
using Azure.AI.OpenAI;

// Define the agent
OpenAIResponseAgent agent = new(new AzureOpenAIClient(endpoint, new AzureCliCredential()))
{
    Name = "ResponseAgent",
    Instructions = "Answer all queries in English and French.",
};

// Initial thread can be null as it will be automatically created
AgentThread? agentThread = null;

var responseItems = agent.InvokeAsync(new ChatMessageContent(AuthorRole.User, "Input message."), agentThread);
await foreach (AgentResponseItem<ChatMessageContent> responseItem in responseItems)
{
    // Update the thread to maintain the conversation for future interaction
    agentThread = responseItem.Thread;

    WriteAgentChatMessage(responseItem.Message);
}
```

**After (Agent Framework):**

Agent Framework automatically manages the thread, so there's no need to manually update it.

```csharp
using Microsoft.Extensions.AI.Agents.OpenAI;
using Azure.AI.OpenAI;

AIAgent agent = new AzureOpenAIClient(endpoint, new AzureCliCredential())
    .GetOpenAIResponseClient(modelId)
    .CreateAIAgent(
        name: "ResponseAgent",
        instructions: "Answer all queries in English and French.",
        tools: [/* AITools */]);

AgentThread thread = agent.GetNewThread();

var result = await agent.RunAsync(userInput, thread);

// The thread will be automatically updated with the new response id from this point
```

### 8. A2A Migration

**Semantic Kernel Packages:**

```xml
<PackageReference Include="Microsoft.SemanticKernel.Agents.A2A" />
```

**Agent Framework Packages:**

```xml
<PackageReference Include="Microsoft.Extensions.AI.Agents.A2A" />
```

**Before (Semantic Kernel):**

```csharp
using A2A;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.A2A;

using var httpClient = CreateHttpClient();
var client = new A2AClient(agentUrl, httpClient);
var cardResolver = new A2ACardResolver(url, httpClient);
var agentCard = await cardResolver.GetAgentCardAsync();
Console.WriteLine(JsonSerializer.Serialize(agentCard, s_jsonSerializerOptions));
var agent = new A2AAgent(client, agentCard);
```

**After (Agent Framework):**

```csharp
using System;
using A2A;
using Microsoft.Extensions.AI.Agents;
using Microsoft.Extensions.AI.Agents.A2A;

var a2aAgentHost = Environment.GetEnvironmentVariable("A2A_AGENT_HOST") ?? throw new InvalidOperationException("A2A_AGENT_HOST is not set.");

// Initialize an A2ACardResolver to get an A2A agent card.
A2ACardResolver agentCardResolver = new(new Uri(a2aAgentHost));

// Create an instance of the AIAgent for an existing A2A agent specified by the agent card.
AIAgent agent = await agentCardResolver.GetAIAgentAsync();
```

### 9. Unsupported Providers (Require Custom Implementation)

#### BedrockAgent Migration

**Status**: Hosted Agents is not directly supported in Agent Framework

**Status**: Non-Hosted AI Model Agents supported via `ChatClientAgent`

**Semantic Kernel (Before):**

```csharp
using Microsoft.SemanticKernel.Agents.Bedrock;

// Create a new agent on the Bedrock Agent service and prepare it for use
using var client =  new AmazonBedrockAgentClient();
using var runtimeClient = new AmazonBedrockAgentRuntimeClient();
var agentModel = await client.CreateAndPrepareAgentAsync(new CreateAgentRequest()
    {
        AgentName = agentName,
        Description = "AgentDescription",
        Instruction = "You are a helpful assistant",
        AgentResourceRoleArn = TestConfiguration.BedrockAgent.AgentResourceRoleArn,
        FoundationModel = TestConfiguration.BedrockAgent.FoundationModel,
    });

// Create a new BedrockAgent instance with the agent model and the client
// so that we can interact with the agent using Semantic Kernel contents.
var agent = new BedrockAgent(agentModel, client, runtimeClient);
```

**Agent Framework (After):**

Currently there's no support for the Hosted Bedrock Agent service in Agent Framework.

Alternatively, for providers like AWS Bedrock that already have an `IChatClient` implementation available, you can use the `ChatClientAgent` directly by providing the `IChatClient` instance to the agent. 

_Those agents will be purelly backed by the AI chat models behavior and will not store any state in the server._

```csharp
using Microsoft.Extensions.AI.Agents;

services.TryAddAWSService<IAmazonBedrockRuntime>();
var serviceProvider = services.BuildServiceProvider();
IAmazonBedrockRuntime runtime = serviceProvider.GetRequiredService<IAmazonBedrockRuntime>();

using var bedrockChatClient = runtime.AsIChatClient();
AIAgent agent = new ChatClientAgent(bedrockChatClient, instructions: "You are a helpful assistant");
```

### Provider-Specific Package Reference Updates

When migrating projects, update package references according to the provider being used:

#### Remove Semantic Kernel Packages:
- `Microsoft.SemanticKernel`
- `Microsoft.SemanticKernel.Agents.Abstractions`
- `Microsoft.SemanticKernel.Agents.Core`
- `Microsoft.SemanticKernel.Agents.OpenAI`
- `Microsoft.SemanticKernel.Agents.AzureAI`
- `Microsoft.SemanticKernel.Agents.A2A`

#### Add Agent Framework Packages
- `Microsoft.Extensions.AI.Agents.Abstractions`
- `Microsoft.Extensions.AI.Agents`
- `Microsoft.Extensions.AI.Agents.OpenAI`
- `Microsoft.Extensions.AI.Agents.AzureAI`
- `Microsoft.Extensions.AI.Agents.A2A`

### Provider-Specific Configuration Patterns

Each provider may have specific configuration requirements:

#### OpenAI Provider
```csharp
// API Key configuration
AIAgent agent = new OpenAIClient(apiKey).GetChatClient(modelId).CreateAIAgent(instructions);

// With custom options
ChatClientAgentRunOptions options = new(new ChatOptions
{
    MaxOutputTokens = 1000,
    Temperature = 0.7f,
    Tools = [/* AITools */]
});
```

#### Azure OpenAI Provider

```csharp
// Azure Cli Credential
AIAgent agent = new AzureOpenAIClient(endpoint, new AzureCliCredential())
    .GetChatClient(deploymentName)
    .CreateAIAgent(instructions);

// API Key
AIAgent agent = new AzureOpenAIClient(endpoint, new AzureKeyCredential(apiKey))
    .GetChatClient(deploymentName)
    .CreateAIAgent(instructions);
```

#### A2A Provider

```csharp
// Initialize an A2ACardResolver to get an A2A agent card.
A2ACardResolver agentCardResolver = new(new Uri(a2aAgentHost));

// Create an instance of the AIAgent for an existing A2A agent specified by the agent card.
AIAgent agent = await agentCardResolver.GetAIAgentAsync();
```

This migration guide provides the foundation for successfully transitioning from Semantic Kernel Agents to Agent Framework while maintaining functionality and improving code quality.

### Unsupported Features and Workarounds

The following Semantic Kernel Agents features currently don't have direct equivalents in Agent Framework:

#### Plugins

**Problem**: Semantic Kernel plugins allowed multiple functions to be registered under a type

**Workaround**: Functions need to be provided directly as a flat list of tools to the agent.

#### Prompt Template

**Problem**: Agent prompt templating is not yet supported in Agent Framework

**Workaround**: Use the existing `SemanticKernel` template engine to render the prompt before calling the agents.
