// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable IDE0002 // Simplify Member Access

var apiKey = Environment.GetEnvironmentVariable("GOOGLEAI_API_KEY") ?? throw new InvalidOperationException("GOOGLEAI_API_KEY is not set.");
var model = System.Environment.GetEnvironmentVariable("GOOGLEAI_MODEL") ?? "gemini-2.0-flash";
var userInput = "What is the weather like in Amsterdam?";

Console.WriteLine($"User Input: {userInput}");

[KernelFunction]
[Description("Get the weather for a given location.")]
static string GetWeather([Description("The location to get the weather for.")] string location)
    => $"The weather in {location} is cloudy with a high of 15°C.";

await SKAgentAsync();
await AFAgentAsync();

async Task SKAgentAsync()
{
    var builder = Kernel.CreateBuilder().AddGoogleAIGeminiChatCompletion(model, apiKey);

    ChatCompletionAgent agent = new()
    {
        Instructions = "You are a helpful assistant",
        Kernel = builder.Build(),
        Arguments = new KernelArguments(new GeminiPromptExecutionSettings() { ToolCallBehavior = GeminiToolCallBehavior.AutoInvokeKernelFunctions }),
    };

    // Initialize plugin and add to the agent's Kernel (same as direct Kernel usage).
    agent.Kernel.Plugins.Add(KernelPluginFactory.CreateFromFunctions("KernelPluginName", [KernelFunctionFactory.CreateFromMethod(GetWeather)]));

    Console.WriteLine("\n=== SK Agent Response ===\n");

    var result = await agent.InvokeAsync(userInput).FirstAsync();
    Console.WriteLine(result.Message);
}

async Task AFAgentAsync()
{
    using var googleChatClient = new GoogleChatClientFunctionInvocationAdapter(
        new GoogleChatCompletionServiceFunctionInvocationAdapter(new(model, apiKey))
        .AsChatClient());

    var agent = new ChatClientAgent(googleChatClient,
        instructions: "You are a helpful assistant",
        tools: [AIFunctionFactory.Create(GetWeather)]);

    Console.WriteLine("\n=== AF Agent Response ===\n");

    var result = await agent.RunAsync(userInput);
    Console.WriteLine(result);
}

// Semantic Kernel Google Connector implementation does not support FunctionChoiceBehavior flow which is compatible with Microsoft.Extensions.AI.IChatClients

// Because of that adapting transformations are necessary to interchange between M.E.AI abstractions and SK Google Connector ones.

// The code below demonstrates how to enable non-streaming function call with existing connector.

internal sealed class GoogleChatClientFunctionInvocationAdapter(IChatClient chatClient) : DelegatingChatClient(chatClient)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken = default)
    {
        if (options?.Tools is { Count: > 0 })
        {
            // Before passing the request into Chat Completion Services, a kernel needs to be created as the container of functions and passed into the additional properties
            // So it can be later captured via PromptExecutionSettings into the ExtensionData
            var kernel = new Kernel();
            kernel.Plugins.AddFromFunctions("Tools", options.Tools.OfType<AIFunction>().Select(f => f.AsKernelFunction()).ToList());

            options.AdditionalProperties ??= new();
            options.AdditionalProperties["Kernel"] = kernel;
        }

        return this.InnerClient.GetResponseAsync(messages, options, cancellationToken);
    }
}

internal sealed class GoogleChatCompletionServiceFunctionInvocationAdapter : IChatCompletionService
{
    private readonly GoogleAIGeminiChatCompletionService _chatCompletionService;

    public IReadOnlyDictionary<string, object?> Attributes => this._chatCompletionService.Attributes;

    public GoogleChatCompletionServiceFunctionInvocationAdapter(GoogleAIGeminiChatCompletionService chatCompletionService)
    {
        this._chatCompletionService = chatCompletionService ?? throw new ArgumentNullException(nameof(chatCompletionService));
    }

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        // A compatible chat history is generated for if any downstream chat content that the connector is not familiar to.
        ChatHistory geminiCompatibleChatHistory = [];
        foreach (var message in chatHistory)
        {
            // Convert the Function Call Contents back into GeminiContents
            foreach (var callContent in message.Items.OfType<Microsoft.SemanticKernel.FunctionCallContent>())
            {
                var value = ((ValueTuple<GeminiChatMessageContent, GeminiFunctionToolCall>)callContent.InnerContent!).Item1;

                geminiCompatibleChatHistory.Add(((ValueTuple<GeminiChatMessageContent, GeminiFunctionToolCall>)callContent.InnerContent!).Item1);
            }

            // Convert the Function Result Contents into the expected GeminiContents
            foreach (var result in message.Items.OfType<Microsoft.SemanticKernel.FunctionResultContent>())
            {
                var geminiToolCall = GetFunctionToolCallById(result.CallId)!;

                geminiCompatibleChatHistory.Add(new GeminiChatMessageContent(
                    new GeminiFunctionToolResult(
                        geminiToolCall,
                        functionResult: new FunctionResult(
                            function: KernelFunctionFactory.CreateFromMethod(() => { }),
                            value: result.Result)))
                {
                    Role = AuthorRole.Tool,
                    Content = string.Empty,
                });
            }

            // Normal content messages added without call contents (currently not supported)
            if (message.Items.Any(i => i is Microsoft.SemanticKernel.TextContent or Microsoft.SemanticKernel.ImageContent or Microsoft.SemanticKernel.AudioContent))
            {
                var newMessage = new ChatMessageContent();
                newMessage.Role = message.Role;
                foreach (var validContent in message.Items.Where(i => i is Microsoft.SemanticKernel.TextContent or Microsoft.SemanticKernel.ImageContent or Microsoft.SemanticKernel.AudioContent))
                {
                    newMessage.Items.Add(validContent);
                }
                geminiCompatibleChatHistory.Add(newMessage);
            }
        }

        // Gets the tool call reference from the chat history to pass back to the model
        GeminiFunctionToolCall? GetFunctionToolCallById(string? id)
        {
            if (id is null)
            {
                return null;
            }

            return ((ValueTuple<GeminiChatMessageContent, GeminiFunctionToolCall>)chatHistory
                .SelectMany(c => c.Items)
                .OfType<Microsoft.SemanticKernel.FunctionCallContent>()
                .First(c => c.Id == id)
                .InnerContent!).Item2;
        }

        // Capture the kernel with the functions for execution (Gemini relies strongly on the kernel instance)
        kernel ??= PopKernelFromSettings(executionSettings);

        // Capture the Gemini Message Contents
        var results = (IReadOnlyList<GeminiChatMessageContent>)await this._chatCompletionService.GetChatMessageContentsAsync(geminiCompatibleChatHistory, EnableAutoToolCallBehavior(executionSettings), kernel, cancellationToken);

        // Prepare the tool call abstractions to be handled gracefully by the FunctionInvokingChatClient.
        foreach (var result in results)
        {
            if (result.ToolCalls is { Count: > 0 })
            {
                foreach (var toolCall in result.ToolCalls)
                {
                    KernelArguments? arguments = null;

                    // Add parameters to arguments
                    if (toolCall.Arguments is not null)
                    {
                        arguments = [];
                        foreach (var parameter in toolCall.Arguments)
                        {
                            arguments[parameter.Key] = parameter.Value?.ToString();
                        }
                    }

                    // Create the expected abstraction for a function call request
                    var functionCallContent = new Microsoft.SemanticKernel.FunctionCallContent(
                        toolCall.FunctionName,
                        arguments: arguments,
                        pluginName: toolCall.PluginName,
                        id: Guid.NewGuid().ToString()
                    )
                    // Provides breaking glass options to recover the types for backfilling the chat history later on.
                    { InnerContent = (result, toolCall) };

                    result.Items.Add(functionCallContent);
                }
            }
        }

        return results;
    }

    public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
        => this._chatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory, EnableAutoToolCallBehavior(executionSettings), kernel, cancellationToken);

    // Change the configuration prior to sending to the actual Chat Completion Service.
    private static PromptExecutionSettings EnableAutoToolCallBehavior(PromptExecutionSettings? executionSettings)
    {
        var settings = executionSettings ?? new GeminiPromptExecutionSettings();

        if (settings is GeminiPromptExecutionSettings geminiSettings)
        {
            geminiSettings.ToolCallBehavior = GeminiToolCallBehavior.EnableKernelFunctions;
        }
        else
        {
            // If a different PromptExecutionSettings type is used, create a new GeminiPromptExecutionSettings
            settings = new GeminiPromptExecutionSettings()
            {
                ToolCallBehavior = GeminiToolCallBehavior.EnableKernelFunctions,
                ServiceId = executionSettings!.ServiceId,
                ModelId = executionSettings.ModelId,
                FunctionChoiceBehavior = executionSettings.FunctionChoiceBehavior,
            };
        }

        return settings;
    }

    // Pop the kernel out from the extension attributes
    private static Kernel? PopKernelFromSettings(PromptExecutionSettings? executionSettings)
    {
        if (executionSettings?.ExtensionData is not null && executionSettings.ExtensionData.TryGetValue("Kernel", out var kernelObj) && kernelObj is Kernel kernel)
        {
            executionSettings.ExtensionData.Remove("Kernel");
            return kernel;
        }

        return null;
    }
}
