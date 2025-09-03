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
  - `Microsoft.SemanticKernel.Agents.AzureOpenAI`
  - `Microsoft.SemanticKernel` (if only used for agents)
- Add the appropriate Agent Framework package references to the project file:
  - `Microsoft.Extensions.AI.Agents.Abstractions` (core abstractions)
  - `Microsoft.Extensions.AI.Agents.OpenAI` (for OpenAI providers)
  - `Microsoft.Extensions.AI.Agents.AzureOpenAI` (for Azure OpenAI providers)
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
     - all project dependencies changes (mention what was changed, added or removed)
     - all code files that were changed (mention what was changed in the file, if it was not changed, just mention that the file was not changed)
     - all cases where you could not convert the code because of unsupported features and you were unable to find a workaround
     - all behavioral changes that have to be verified at runtime
     - all follow up steps that user would have to do in the report markdown file

## Detailed information about differences in Semantic Kernel Agents and Agent Framework

The Agent Framework provides functionality for creating and managing AI agents with simplified APIs and better performance. The Agent Framework is included in the Microsoft.Extensions.AI package ecosystem and provides a unified interface across different AI providers.

Agent Framework focuses primarily on simplicity, performance, and developer experience. It has some key differences in default behavior and doesn't aim to have complete feature parity with Semantic Kernel Agents. For some scenarios, Agent Framework currently has different patterns, but there are recommended migration paths. For other scenarios, the migration provides significant improvements in code clarity and maintainability.

The Agent Framework team is investing in adding the features that are most often requested. If your application depends on a missing feature, consider filing an issue in the dotnet/extensions GitHub repository to find out if support for your scenario can be added.

Most of this article is about how to use the AIAgent API, but it also includes guidance on how to use the AgentThread, AIFunction, and other related types.

### Table of differences

The following table lists Semantic Kernel Agents features and Agent Framework equivalents. The equivalents fall into the following categories:

| Semantic Kernel Agents feature                              | Agent Framework equivalent                                   |
|--------------------------------------------------------------|--------------------------------------------------------------|
| Microsoft.SemanticKernel.Agents namespace                   | Microsoft.Extensions.AI.Agents namespace                    |
| ChatCompletionAgent class                                   | ChatClientAgent interface with provider-specific implementations    |
| OpenAIAssistantAgent class                                  | AIAgent with OpenAI Assistants provider                     |
| AzureAIAgent class                                          | AIAgent with Azure AI Foundry provider                      |
| Kernel-based agent creation                                 | Direct client-based agent creation                          |
| KernelFunction and KernelPlugin for tools                   | AIFunction for tools                                         |
| Manual thread creation with specific types                  | agent.GetNewThread() for automatic thread creation          |
| InvokeAsync method                                          | RunAsync method                                              |
| InvokeStreamingAsync method                                 | RunStreamingAsync method                                     |
| AgentInvokeOptions for configuration                        | Provider-specific run options (e.g., ChatClientAgentRunOptions) |
| IAsyncEnumerable<AgentResponseItem<ChatMessageContent>>     | AgentRunResponse for non-streaming                          |
| IAsyncEnumerable<StreamingChatMessageContent>               | IAsyncEnumerable<AgentRunResponseUpdate> for streaming      |
| KernelArguments for execution settings                      | Direct options configuration                                 |
| Complex dependency injection with Kernel                    | Simplified service registration                              |
| [KernelFunction] attribute required for tools               | [Description] attribute sufficient for tools                |
| Plugin-based tool organization                              | Direct function registration                                 |
| AgentThread.DeleteAsync() for cleanup                       | Provider-specific cleanup when needed                       |
| Multiple agent-specific thread types                        | Unified AgentThread interface                                |

This is not an exhaustive list of Semantic Kernel Agents features. The list includes many of the scenarios that are commonly used in agent applications.

### Differences in default behavior

Agent Framework is designed for simplicity and performance by default, emphasizing clear and intuitive APIs. The framework is intentionally designed this way for better developer experience and reduced complexity. Semantic Kernel Agents provided more flexibility but with increased complexity. This fundamental difference in design is behind many of the following specific differences in default behavior.

### Namespace Updates

During migration, Semantic Kernel Agents namespaces need to be updated to Agent Framework namespaces. The Agent Framework uses `Microsoft.Extensions.AI` as the root namespace for all AI-related functionality.

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

**Semantic Kernel Agents** required creating a Kernel instance first, then creating agents with that Kernel:

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

**Agent Framework** simplifies this to a single fluent call:

```csharp
AIAgent agent = new OpenAIClient(apiKey)
    .GetChatClient(modelId)
    .CreateAIAgent(instructions: "You are a helpful assistant");
```

### Thread Management Changes

**Semantic Kernel Agents** required manual thread creation with specific types:

```csharp
// Different thread types for different scenarios
AgentThread thread = new ChatHistoryAgentThread();
AgentThread thread = new OpenAIAssistantAgentThread(assistantClient);
AgentThread thread = new AzureAIAgentThread(azureClient);
```

**Agent Framework** provides unified thread creation through the agent:

```csharp
AgentThread thread = agent.GetNewThread();
```

### Tool Registration Differences

**Semantic Kernel Agents** required complex plugin setup:

```csharp
[KernelFunction]
[Description("Get the weather for a location")]
static string GetWeather(string location) => $"Weather in {location}";

KernelFunction function = KernelFunctionFactory.CreateFromMethod(GetWeather);
KernelPlugin plugin = KernelPluginFactory.CreateFromFunctions("WeatherPlugin", [function]);
kernel.Plugins.Add(plugin);

ChatCompletionAgent agent = new() { Kernel = kernel };
```

**Agent Framework** allows direct function registration:

```csharp
[Description("Get the weather for a location")]
static string GetWeather(string location) => $"Weather in {location}";

AIAgent agent = chatClient.CreateAIAgent(
    tools: [AIFunctionFactory.Create(GetWeather)]);
```

### Invocation Method Changes

**Semantic Kernel Agents** used `InvokeAsync` and `InvokeStreamingAsync`:

```csharp
// Non-streaming
await foreach (var result in agent.InvokeAsync(userInput, thread, options))
{
    Console.WriteLine(result.Message);
}

// Streaming
await foreach (var update in agent.InvokeStreamingAsync(userInput, thread, options))
{
    Console.Write(update.Message);
}
```

**Agent Framework** uses `RunAsync` and `RunStreamingAsync`:

```csharp
// Non-streaming
AgentRunResponse result = await agent.RunAsync(userInput, thread, options);
Console.WriteLine(result);

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
```

### Unsupported Features and Workarounds

Some Semantic Kernel Agents features don't have direct equivalents in Agent Framework:

#### Complex Plugin Hierarchies
**Problem**: Semantic Kernel supported complex plugin organization with namespaces
**Workaround**: Use flat function registration with descriptive names

#### Custom Kernel Services
**Problem**: Semantic Kernel allowed custom service registration in Kernel
**Workaround**: Use dependency injection at the application level

#### Advanced Execution Settings
**Problem**: Some advanced Kernel execution settings may not be available
**Workaround**: Use provider-specific options or configure at the client level

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
Remove `[KernelFunction]` attributes and ensure `[Description]` attributes are present.

#### Thread Type Mismatches
Replace specific thread types with unified `AgentThread` interface.

#### Options Configuration
Update from `AgentInvokeOptions` to provider-specific run options.

#### Dependency Injection
Simplify service registration by removing Kernel dependencies.

### Best Practices for Migration

1. **Incremental Migration**: Migrate one agent at a time
2. **Test Coverage**: Ensure comprehensive testing of migrated functionality
3. **Documentation**: Update code comments and documentation
4. **Error Handling**: Review and update exception handling patterns
5. **Performance Testing**: Validate performance improvements
6. **Code Review**: Have migrated code reviewed by team members

This migration guide provides the foundation for successfully transitioning from Semantic Kernel Agents to Agent Framework while maintaining functionality and improving code quality.