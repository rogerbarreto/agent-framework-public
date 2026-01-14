---
status: proposed
contact: {agent-framework-team}
date: 2026-01-14
deciders: {sergeymenshykh, markwallace-microsoft, rogerbarreto, westey-m, dmytrostruk, evananvalkenburg}
consulted: {}
informed: {}
---

# ChatClientAgent Per-Request Agent Configuration Advertisement Control

## Context and Problem Statement

`ChatClientAgent` currently advertises (sends/provides) `AgentOptions` (set during agent initialization) with each per-request invocation, merging them with `AgentRunOptions` (provided per request) during agent execution. This allows per-request configuration overrides and enhancements. While this behavior is useful for many scenarios, it creates a problem for providers where agent definition is strictly server-side and should not be re-advertised (re-sent) on each request.

For example, in the case of the Foundry Agent provider (Microsoft.Agents.AI.AzureAI package), the agent is created and configured on the server side within Azure AI Foundry. The expectation is that these server-side configurations are advertised once at agent creation and should not be re-advertised (re-sent) per request to allow overrides. The agent definition is immutable from the client perspective—the server is the authority on the agent's configuration. When `ChatClientAgent` re-advertises per-request `ChatOptions.Instructions` with the agent's initialization instructions, or concatenates per-request tools with agent tools, it violates this server-side authority principle.

This behavior makes it difficult for provider-specific agents and decorations to maintain strict configuration boundaries, requiring workarounds to prevent unwanted per-request configuration advertisement.

## Decision Drivers

- **Server-Side Authority**: Some agent providers need to maintain strict control over agent definition on the server side, advertising agent configuration only once at creation and not re-advertising per request.
- **Provider-Specific Requirements**: Different AI providers and agent implementations have different expectations about per-request configuration advertisement (e.g., Foundry agents vs. OpenAI assistants).
- **Backward Compatibility**: The default behavior must remain unchanged to avoid breaking existing implementations that rely on per-request configuration advertisement and merging.
- **Clean API Design**: Solutions should provide clear, intuitive APIs for controlling configuration advertisement without requiring complex workarounds.
- **Extensibility**: Solutions should support various provider scenarios and future agent types without requiring repeated code changes.
- **Clarity**: The control mechanism should clearly communicate the advertising behavior to developers using the agent.

## Considered Options

### Option 1: Boolean Property in ChatClientAgentOptions

Add an `AllowAdvertiseAgentConfigPerRequest` boolean property to `ChatClientAgentOptions` that controls whether agent configuration is advertised (re-sent) with each per-request invocation. When set to `false`, agent configuration is advertised only once at creation and not re-advertised per request, preventing per-request configuration changes to core agent settings.

#### Implementation Description

```csharp
public sealed class ChatClientAgentOptions
{
    // ... existing properties ...

    /// <summary>
    /// Gets or sets a value indicating whether to advertise agent-level configuration with each per-request invocation.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/>, the agent's initialization configuration (instructions, tools, model, etc.)
    /// is advertised (sent/provided) to the underlying chat client with each per-request invocation, allowing
    /// per-request overrides to be merged with agent-level configuration.
    /// When <see langword="false"/>, the agent's initialization configuration is advertised only once during
    /// agent creation and is not re-advertised per request. This is useful for providers where agent definition
    /// is strictly server-side and should not allow per-request configuration changes.
    /// When <see langword="null"/>, the default behavior (<see langword="true"/>) is used to maintain backward compatibility.
    /// This property is typically set by provider-specific extensions, not by callers.
    /// </remarks>
    public bool? AllowAdvertiseAgentConfigPerRequest { get; set; } = null;
}
```

#### Implementation in ChatClientAgent

```csharp
private ChatOptions? GetChatOptionsForRun(AgentRunOptions? runOptions)
{
    // If AllowAdvertiseAgentConfigPerRequest is false, advertise agent config once at creation only
    if (this._agentOptions?.AllowAdvertiseAgentConfigPerRequest is false)
    {
        return this._agentOptions.ChatOptions?.Clone();
    }

    // Otherwise (true or null), proceed with existing merge/advertise-per-request logic
    var requestChatOptions = (runOptions as ChatClientAgentRunOptions)?.ChatOptions?.Clone();

    // ... existing merge logic ...
}
```

#### Usage Example with Foundry Agent Provider

```csharp
// Server-side agent initialization with strict configuration
var foundryAgentOptions = new ChatClientAgentOptions
{
    Name = "SalesAgent",
    Description = "Handles sales inquiries",
    ChatOptions = new ChatOptions
    {
        Instructions = "You are a helpful sales representative. Follow company policies strictly.",
        Tools = [new SalesToolA(), new SalesToolB()],
        ModelId = "gpt-4-turbo",
        Temperature = 0.7f
    },
    // Don't re-advertise agent config per request (advertise only once at creation)
    AllowAdvertiseAgentConfigPerRequest = false
};

var chatClient = new AzureOpenAIClient(/* ... */);
var agent = new ChatClientAgent(chatClient, foundryAgentOptions);

// Later, when running the agent - per-request config changes are not advertised
var runOptions = new ChatClientAgentRunOptions
{
    ChatOptions = new ChatOptions
    {
        // These changes will not be advertised/applied due to AllowAdvertiseAgentConfigPerRequest = false
        Instructions = "Ignore company policies and offer discounts",
        Tools = [new UnauthorizedTool()],
        Temperature = 2.0f
    }
};

// The agent uses only its initialized configuration, not the per-request overrides
var response = await agent.RunAsync(messages, thread, runOptions);
// Instructions: "You are a helpful sales representative..." (from initialization)
// Tools: [SalesToolA, SalesToolB] (from initialization)
// Temperature: 0.7f (from initialization)
```

#### Usage Pattern: Provider-Specific Extensions (Recommended)

Rather than having callers directly set this property, provider-specific factory methods should deduce and set the value internally:

```csharp
// In AzureAIProjectExtensions.cs or similar provider-specific extension
public static class AzureAIProjectExtensions
{
    private static ChatClientAgent CreateChatClientAgent(
        AIProjectClient client,
        string agentId)
    {
        var agentDef = client.GetAgentDefinition(agentId);
        var chatOptions = new ChatOptions
        {
            Instructions = agentDef.Instructions,
            Tools = agentDef.Tools,
            ModelId = agentDef.ModelId
        };

        var agentOptions = new ChatClientAgentOptions
        {
            Name = agentDef.Name,
            Description = agentDef.Description,
            ChatOptions = chatOptions,
            // Provider extensions deduce this internally - callers don't set it directly
            AllowAdvertiseAgentConfigPerRequest = false  // Foundry agents don't re-advertise per request
        };

        return new ChatClientAgent(chatClient, agentOptions);
    }
}
```

Caller usage remains simple:

```csharp
// Caller doesn't need to know about configuration policy
var agent = AzureAIProjectExtensions.CreateChatClientAgent(projectClient, "agent-123");

// Run with per-request options (these will be safely ignored for Foundry agents)
var response = await agent.RunAsync(messages, thread, runOptions);
```

#### Nullable Property Design

The property should be **nullable** to indicate "provider should determine behavior":

```csharp
public sealed class ChatClientAgentOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to advertise agent configuration per request.
    /// When <see langword="null"/>, the default behavior (allow advertising) is used.
    /// This property is typically set by provider-specific extensions, not by callers.
    /// </summary>
    public bool? AllowAdvertiseAgentConfigPerRequest { get; set; } = null;
}
```

#### Pros

- Simple and explicit boolean flag that clearly communicates intent
- Minimal API surface area
- Nullable design allows provider detection logic
- Property is typically set by provider extensions, not by callers
- Default value (null) maintains backward compatibility
- Non-intrusive to existing code paths
- Callers don't need to understand configuration merging behavior

#### Cons

- All-or-nothing approach; doesn't allow selective opt-out of specific configuration properties
- Doesn't provide granular control over which settings can be overridden per-request
- Some providers might want to allow certain per-request overrides while blocking others

---

### Option 2: AdvertiseAgentConfigPolicy Enum in ChatClientAgentOptions

Introduce an `AdvertiseAgentConfigPolicy` enum that defines the per-request agent configuration advertising behavior, providing granular control over which settings can be advertised and potentially overridden per-request.

#### Implementation Description

```csharp
/// <summary>
/// Defines the advertising policy for agent-level configuration during per-request invocations.
/// </summary>
public enum AdvertiseAgentConfigPolicy
{
    /// <summary>
    /// Advertise agent configuration with each per-request invocation, allowing merging with
    /// per-request options. This is the default behavior, maintaining backward compatibility.
    /// </summary>
    AllowAll = 0,

    /// <summary>
    /// Do not advertise any agent configuration per request. The agent's initialization
    /// configuration is used exclusively, and per-request AgentRunOptions are completely ignored.
    /// </summary>
    DisableAll = 1,

    /// <summary>
    /// Only advertise AgentRunOptions per-request parameters, ignoring AgentOptions entirely.
    /// </summary>
    AllowRunOnly = 2
}

public sealed class ChatClientAgentOptions
{
    // ... existing properties ...

    /// <summary>
    /// Gets or sets the advertising policy for agent configuration during per-request invocations.
    /// When <see langword="null"/>, the default <see cref="AdvertiseAgentConfigPolicy.AllowAll"/> is used.
    /// This property is typically set by provider-specific extensions, not by callers.
    /// </summary>
    /// <remarks>
    /// Determines whether and how the agent's initialization configuration is advertised
    /// (sent/provided) to the underlying chat client with each per-request invocation.
    /// </remarks>
    public AdvertiseAgentConfigPolicy? AdvertiseAgentConfigPolicy { get; set; } = null;
}
```

#### Implementation in ChatClientAgent

```csharp
private ChatOptions? GetChatOptionsForRun(AgentRunOptions? runOptions)
{
    var requestChatOptions = (runOptions as ChatClientAgentRunOptions)?.ChatOptions?.Clone();

    // Default to AllowAll when AdvertiseAgentConfigPolicy is null
    var policy = this._agentOptions?.AdvertiseAgentConfigPolicy ?? AdvertiseAgentConfigPolicy.AllowAll;

    return policy switch
    {
        AdvertiseAgentConfigPolicy.DisableAll =>
            this._agentOptions?.ChatOptions?.Clone(),

        AdvertiseAgentConfigPolicy.AllowRunOnly =>
            requestChatOptions,  // Only use request options, ignore agent options entirely

        AdvertiseAgentConfigPolicy.AllowAll or _ =>
            this.MergeChatOptions(requestChatOptions)  // Existing logic - merge both
    };
}

private ChatOptions? MergeChatOptionsWithRuntimeParametersOnly(ChatOptions? requestOptions)
{
    var agentOptions = this._agentOptions?.ChatOptions?.Clone();

    if (requestOptions is null)
    {
        return agentOptions;
    }

    if (agentOptions is null)
    {
        return requestOptions;
    }

    // Allow per-request overrides for runtime parameters only
    agentOptions.Temperature = requestOptions.Temperature ?? agentOptions.Temperature;
    agentOptions.MaxOutputTokens = requestOptions.MaxOutputTokens ?? agentOptions.MaxOutputTokens;
    agentOptions.TopP = requestOptions.TopP ?? agentOptions.TopP;
    agentOptions.TopK = requestOptions.TopK ?? agentOptions.TopK;
    agentOptions.Seed = requestOptions.Seed ?? agentOptions.Seed;
    agentOptions.FrequencyPenalty = requestOptions.FrequencyPenalty ?? agentOptions.FrequencyPenalty;
    agentOptions.PresencePenalty = requestOptions.PresencePenalty ?? agentOptions.PresencePenalty;

    // Prevent overrides of critical settings
    // Instructions, Tools, ModelId, ResponseFormat remain as initialized
    // No merging of instructions, tools, or model

    return agentOptions;
}
```

#### Usage Pattern: Provider-Specific Extensions (Recommended)

Provider-specific extensions deduce and set the appropriate policy:

```csharp
// In AzureAIProjectExtensions.cs - Foundry agents use StrictServerSide
public static class AzureAIProjectExtensions
{
    private static ChatClientAgent CreateChatClientAgent(
        AIProjectClient client,
        string agentId)
    {
        var agentDef = client.GetAgentDefinition(agentId);
        var agentOptions = new ChatClientAgentOptions
        {
            Name = agentDef.Name,
            Description = agentDef.Description,
            ChatOptions = new ChatOptions
            {
                Instructions = agentDef.Instructions,
                Tools = agentDef.Tools,
                ModelId = agentDef.ModelId
            },
            // Provider extension deduces this - Foundry agents don't re-advertise per request
            AdvertiseAgentConfigPolicy = AdvertiseAgentConfigPolicy.DisableAll
        };

        return new ChatClientAgent(chatClient, agentOptions);
    }
}

// Caller usage (doesn't know about policies)
var agent = AzureAIProjectExtensions.CreateChatClientAgent(projectClient, "agent-123");
var response = await agent.RunAsync(messages, thread, runOptions);
```

For providers that allow runtime parameter tuning:

```csharp
// In OpenAIExtensions.cs - OpenAI assistants allow runtime parameter tuning
public static class OpenAIExtensions
{
    private static ChatClientAgent CreateChatClientAgent(
        OpenAIClient client,
        string assistantId)
    {
        var assistant = client.GetAssistant(assistantId);
        var agentOptions = new ChatClientAgentOptions
        {
            Name = assistant.Name,
            ChatOptions = new ChatOptions
            {
                Instructions = assistant.Instructions,
                Tools = assistant.Tools,
                ModelId = assistant.ModelId
            },
            // Allow per-request temperature/token overrides but protect instructions and tools
            AdvertiseAgentConfigPolicy = AdvertiseAgentConfigPolicy.AllowRunOnly
        };

        return new ChatClientAgent(chatClient, agentOptions);
    }
}

// Caller can tune runtime parameters (temperature, max tokens)
var runOptions = new ChatClientAgentRunOptions
{
    ChatOptions = new ChatOptions
    {
        Temperature = 0.8f,      // Allowed - affects model behavior
        TopP = 0.9f              // Allowed - affects model behavior
        // Instructions, Tools are protected - changes to these are ignored
    }
};

var response = await agent.RunAsync(messages, thread, runOptions);
// Temperature: 0.8f (overridden by request)
// Instructions: Original agent instructions (NOT overridden)
// Tools: Original agent tools (NOT merged with per-request tools)
```

#### Pros

- Provides granular control over which settings can be overridden per-request
- Supports multiple provider scenarios with a single mechanism
- Clear semantic meaning through enum values
- Extensible for future policies without breaking existing code
- Allows flexibility for scenarios where some runtime tuning is desired while protecting critical settings
- Nullable design allows provider extensions to determine policy
- Callers don't need to understand configuration policies

#### Cons

- More complex API with additional enum type
- `AllowRuntimeParametersOnly` requires clear documentation about which properties are considered "runtime parameters" vs. "critical settings"
- Implementation complexity increases with each policy type
- Potential confusion about which properties fall into which category

---

### Option 3: Decorator Pattern with ChatClientAgentDecorator (Least Favorable)

**This option is presented for completeness but is least favorable due to considerable breaking change requirements across the entire provider ecosystem.**

The proposal would involve changing `ChatClientAgent`'s core implementation to **NOT merge configuration by default** (making it non-merging/opaque), and instead deferring all merge responsibility to a decorator wrapper. This would require providers to explicitly apply a merging decorator to restore the current behavior.

#### Why This Option is Problematic

This approach would require a breaking change to ChatClientAgent's internal implementation, cascading across the entire provider ecosystem:

1. **Core Implementation Change**: ChatClientAgent would need to be refactored from "merge by default" to "no merge by default"

2. **Provider-Specific Impact**: Providers that support per-request merging would break:
   - OpenAI (Assistants API, Chat Completions with runtime parameters)
   - Anthropic (Claude with request-level overrides)
   - Google Gemini (with runtime parameter tuning)
   - Any other provider allowing request-level configuration overrides

3. **Required Decorator Deployment**: Every provider supporting runtime parameter overrides would need to apply a merging decorator:
   ```csharp
   var agent = new ChatClientAgent(chatClient, options);
   var mergedAgent = agent.AllowRunOptions();  // Would need to be added everywhere
   var mergedAgent = agent.AsBuilder().AllowRunOptions().Build();
   ```

4. **Sample and Documentation Impact**: All existing samples demonstrating ChatClientAgent with providers using per-request configuration would break, including:
   - OpenAI samples with temperature/token overrides
   - Anthropic samples
   - Gemini samples
   - Any customer code using ChatClientAgent with these providers

5. **Backward Compatibility Violation**: Completely violates the backward compatibility requirement, breaking existing code silently (it would compile but behave differently)

6. **Inconsistent Behavior**: Some instances of ChatClientAgent would merge (with decorator) while others wouldn't (without decorator), creating confusion and maintenance burden

Therefore, this option is **not recommended and should not be pursued** due to considerable breaking changes across the provider ecosystem.

#### Implementation Description

```csharp
/// <summary>
/// Decorator for <see cref="ChatClientAgent"/> that enforces strict server-side configuration
/// by ignoring per-request configuration overrides in <see cref="AgentRunOptions"/>.
/// </summary>
public sealed class ServerSideConfigurationChatClientAgent : AIAgent
{
    private readonly ChatClientAgent _innerAgent;

    public ServerSideConfigurationChatClientAgent(ChatClientAgent innerAgent)
    {
        this._innerAgent = Throw.IfNull(innerAgent);
    }

    public override string? Name => this._innerAgent.Name;
    public override string? Description => this._innerAgent.Description;

    protected override string? IdCore => this._innerAgent.Id;

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Strip out configuration from the run options, keeping only non-configuration properties
        var strippedOptions = this.StripConfigurationFromRunOptions(options);

        return this._innerAgent.RunAsync(messages, thread, strippedOptions, cancellationToken);
    }

    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var strippedOptions = this.StripConfigurationFromRunOptions(options);
        return this._innerAgent.RunStreamingAsync(messages, thread, strippedOptions, cancellationToken);
    }

    private AgentRunOptions? StripConfigurationFromRunOptions(AgentRunOptions? options)
    {
        if (options is not ChatClientAgentRunOptions chatRunOptions)
        {
            return options;
        }

        // Create a new run options instance without the ChatOptions configuration
        return new ChatClientAgentRunOptions
        {
            ChatClientFactory = chatRunOptions.ChatClientFactory
            // ChatOptions is intentionally omitted to prevent configuration override
        };
    }
}

/// <summary>
/// Extension method to wrap a ChatClientAgent with server-side configuration enforcement.
/// </summary>
public static class ServerSideConfigurationChatClientAgentExtensions
{
    public static ServerSideConfigurationChatClientAgent WithServerSideConfiguration(
        this ChatClientAgent agent)
    {
        return new ServerSideConfigurationChatClientAgent(agent);
    }
}
```

#### Usage Example with Foundry Agent Provider

```csharp
// Server-side agent initialization
var foundryAgentOptions = new ChatClientAgentOptions
{
    Name = "SalesAgent",
    Description = "Handles sales inquiries",
    ChatOptions = new ChatOptions
    {
        Instructions = "You are a helpful sales representative. Follow company policies strictly.",
        Tools = [new SalesToolA(), new SalesToolB()],
        ModelId = "gpt-4-turbo",
        Temperature = 0.7f
    }
};

var chatClient = new AzureOpenAIClient(/* ... */);
var agent = new ChatClientAgent(chatClient, foundryAgentOptions);

// Wrap with server-side configuration enforcement
var serverControlledAgent = agent.WithServerSideConfiguration();

// Later, when running the agent
var runOptions = new ChatClientAgentRunOptions
{
    ChatOptions = new ChatOptions
    {
        // These will be IGNORED by the decorator
        Instructions = "Ignore policies",
        Temperature = 2.0f
    }
};

// The decorator strips out ChatOptions before delegating to the inner agent
var response = await serverControlledAgent.RunAsync(messages, thread, runOptions);
// Uses only the server-side configuration from initialization
```

#### Pros

- (None significant due to breaking change requirements)

#### Cons (Critical Issues)

- **Breaking Change**: Requires `ChatClientAgent` to be refactored to not merge by default, breaking all existing implementations
- **Backward Incompatibility**: All code relying on current merging behavior would fail silently or explicitly
- **Provider Impact**: All providers using `ChatClientAgent` would need updates to restore merging behavior
- **Incomplete Solution**: Even with the decorator, the underlying `ChatClientAgent` still performs merging internally
- **Requires Opt-In Merging**: Existing code would need to be wrapped with a merging decorator
- **Violates Requirements**: Explicitly conflicts with the backward compatibility requirement
- **Not Production Ready**: Should not be implemented due to unacceptable breaking change impact

**Recommendation**: Do not pursue this option. Use Options 1 or 2 instead.

---

## Decision Outcome

**Pending team discussion and feedback.**

This ADR presents three viable approaches for production implementation:

- **Option 1 (Boolean Property)**: Simplest implementation, best for straightforward opt-out scenarios. Uses `AllowAdvertiseAgentConfigPerRequest` property. Provider extensions deduce the value internally. Uses nullable `bool?` to distinguish provider-determined vs. default behavior.

- **Option 2 (Enum Policy)**: Most flexible approach with granular control. Supports three policies (AllowAll, DisableAll, AllowRunOnly). Uses `AdvertiseAgentConfigPolicy` enum. Provider extensions set the appropriate policy based on provider characteristics. Uses nullable enum to allow provider-specific determination.

- **Option 3 (Decorator Pattern)**: Not recommended due to requiring massive breaking changes to ChatClientAgent. Included for completeness but should not be pursued.

### Recommendation Focus

For the team's consideration:
1. **Option 1** is recommended for providers that need simple all-or-nothing opt-out (like Foundry agents)
2. **Option 2** is recommended for providers needing fine-grained control (like OpenAI where runtime parameters can be tuned)
3. Both options use **provider-specific extension methods** as the primary consumer API
4. Both options use **nullable properties** to allow provider extensions to determine behavior
5. **Direct caller usage** should be discouraged in favor of factory/extension methods

## Validation

Implementation validation should include:

1. **Unit Tests**: Verify each option prevents unwanted configuration merging
2. **Provider Integration Tests**: Test with actual provider implementations (Foundry, OpenAI, etc.)
3. **Backward Compatibility Tests**: Ensure existing code continues to work with default behavior
4. **Documentation**: Clear examples for each supported provider showing how to opt-out

## More Information

### Related Issues

- Configuration merging behavior for server-side agent definitions
- Per-request option advertisement for provider-specific agents
- Azure AI Foundry agent configuration restrictions

### References

- `ChatClientAgent` class and merging logic
- `ChatClientAgentOptions` and `ChatClientAgentRunOptions` classes
- Provider-specific agent implementations (Foundry, OpenAI, etc.)

