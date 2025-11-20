// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentConformance.IntegrationTests;
using AgentConformance.IntegrationTests.Support;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Shared.IntegrationTests;

namespace AnthropicChatCompletion.IntegrationTests;

public class AnthropicChatCompletionFixture : IChatClientAgentFixture
{
    private static readonly AnthropicConfiguration s_config = TestConfiguration.LoadSection<AnthropicConfiguration>();
    private readonly bool _useReasoningModel;

    private ChatClientAgent _agent = null!;

    public AnthropicChatCompletionFixture(bool useReasoningChatModel)
    {
        this._useReasoningModel = useReasoningChatModel;
    }

    public AIAgent Agent => this._agent;

    public IChatClient ChatClient => this._agent.ChatClient;

    public async Task<List<ChatMessage>> GetChatHistoryAsync(AgentThread thread)
    {
        var typedThread = (ChatClientAgentThread)thread;

        return typedThread.MessageStore is null ? [] : (await typedThread.MessageStore.GetMessagesAsync()).ToList();
    }

    public Task<ChatClientAgent> CreateChatClientAgentAsync(
        string name = "HelpfulAssistant",
        string instructions = "You are a helpful assistant.",
        IList<AITool>? aiTools = null)
    {
        var chatClient = new AnthropicClient() { APIKey = s_config.ApiKey }
            .AsIChatClient(defaultModelId: this._useReasoningModel ? s_config.ChatReasoningModelId : s_config.ChatModelId)
            .AsBuilder()
            .ConfigureOptions(options
                 => options.RawRepresentationFactory = _
                 => new MessageCreateParams()
                 {
                     Model = options.ModelId!,
                     MaxTokens = options.MaxOutputTokens ?? 4096,
                     Messages = [],
                     Thinking = this._useReasoningModel
                        ? new BetaThinkingConfigParam(new BetaThinkingConfigEnabled(2048))
                        : new BetaThinkingConfigParam(new BetaThinkingConfigDisabled())
                 }).Build();

        return Task.FromResult(new ChatClientAgent(chatClient, options: new()
        {
            Name = name,
            Instructions = instructions,
            ChatOptions = new() { Tools = aiTools }
        }));
    }

    public Task DeleteAgentAsync(ChatClientAgent agent) =>
        // Chat Completion does not require/support deleting agents, so this is a no-op.
        Task.CompletedTask;

    public Task DeleteThreadAsync(AgentThread thread) =>
        // Chat Completion does not require/support deleting threads, so this is a no-op.
        Task.CompletedTask;

    public async Task InitializeAsync() =>
        this._agent = await this.CreateChatClientAgentAsync();

    public Task DisposeAsync() =>
        Task.CompletedTask;
}
