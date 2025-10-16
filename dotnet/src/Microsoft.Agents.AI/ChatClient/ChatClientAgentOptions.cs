// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// Represents metadata for a chat client agent, including its identifier, name, instructions, and description.
/// </summary>
/// <remarks>
/// This class is used to encapsulate information about a chat client agent, such as its unique
/// identifier, display name, operational instructions, and a descriptive summary. It can be used to store and transfer
/// agent-related metadata within a chat application.
/// </remarks>
public class ChatClientAgentOptions
{
    private ChatOptions? _chatOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatClientAgentOptions"/> class.
    /// </summary>
    public ChatClientAgentOptions()
    {
    }

    /// <summary>
    /// Gets or sets the agent id.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the agent name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the agent instructions.
    /// </summary>
    public string? Instructions
    {
        get => this._chatOptions?.Instructions;
        set
        {
            if (value is null && this._chatOptions is null)
            {
                // No instructions to set and no existing chat options, nothing to do.
                return;
            }

            this._chatOptions ??= new();
            this._chatOptions.Instructions = value;
        }
    }

    /// <summary>
    /// Gets or sets the agent description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the default chatOptions to use.
    /// </summary>
    /// <remarks>
    /// When providing instructions via both <see cref="Instructions"/> and <see cref="ChatOptions"/>,
    /// will result in a new instruction different lines with the <see cref="Instructions"/> first.
    /// </remarks>
    public ChatOptions? ChatOptions
    {
        get => this._chatOptions;
        set
        {
            var providedOptions = value;
            if (providedOptions is not null)
            {
                // Ensure immutable copy of provided options.
                providedOptions = providedOptions.Clone();
            }

            if (this._chatOptions is not null && providedOptions is not null && string.IsNullOrWhiteSpace(providedOptions.Instructions))
            {
                // Preserve existing agent options instructions if new ChatOptions does not have instructions set.
                providedOptions.Instructions = this._chatOptions.Instructions;
            }

            this._chatOptions = providedOptions;
        }
    }

    /// <summary>
    /// Gets or sets a factory function to create an instance of <see cref="ChatMessageStore"/>
    /// which will be used to store chat messages for this agent.
    /// </summary>
    public Func<ChatMessageStoreFactoryContext, ChatMessageStore>? ChatMessageStoreFactory { get; set; }

    /// <summary>
    /// Gets or sets a factory function to create an instance of <see cref="AIContextProvider"/>
    /// which will be used to create a context provider for each new thread, and can then
    /// provide additional context for each agent run.
    /// </summary>
    public Func<AIContextProviderFactoryContext, AIContextProvider>? AIContextProviderFactory { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to use the provided <see cref="IChatClient"/> instance as is,
    /// without applying any default decorators.
    /// </summary>
    /// <remarks>
    /// By default the <see cref="ChatClientAgent"/> applies decorators to the provided <see cref="IChatClient"/>
    /// for doing for example automatic function invocation. Setting this property to <see langword="true"/>
    /// disables adding these default decorators.
    /// Disabling is recommended if you want to decorate the <see cref="IChatClient"/> with different decorators
    /// than the default ones. The provided <see cref="IChatClient"/> instance should then already be decorated
    /// with the desired decorators.
    /// </remarks>
    public bool UseProvidedChatClientAsIs { get; set; }

    /// <summary>
    /// Creates a new instance of <see cref="ChatClientAgentOptions"/> with the same values as this instance.
    /// </summary>
    internal ChatClientAgentOptions Clone()
        => new()
        {
            Id = this.Id,
            Name = this.Name,
            Instructions = this.Instructions,
            Description = this.Description,
            ChatOptions = this.ChatOptions?.Clone(),
            ChatMessageStoreFactory = this.ChatMessageStoreFactory,
            AIContextProviderFactory = this.AIContextProviderFactory,
        };

    /// <summary>
    /// Context object passed to the <see cref="AIContextProviderFactory"/> to create a new instance of <see cref="AIContextProvider"/>.
    /// </summary>
    public class AIContextProviderFactoryContext
    {
        /// <summary>
        /// Gets or sets the serialized state of the <see cref="AIContextProvider"/>, if any.
        /// </summary>
        /// <value><see langword="default"/> if there is no state, e.g. when the <see cref="AIContextProvider"/> is first created.</value>
        public JsonElement SerializedState { get; set; }

        /// <summary>
        /// Gets or sets the JSON serialization options to use when deserializing the <see cref="SerializedState"/>.
        /// </summary>
        public JsonSerializerOptions? JsonSerializerOptions { get; set; }
    }

    /// <summary>
    /// Context object passed to the <see cref="ChatMessageStoreFactory"/> to create a new instance of <see cref="ChatMessageStore"/>.
    /// </summary>
    public class ChatMessageStoreFactoryContext
    {
        /// <summary>
        /// Gets or sets the serialized state of the chat message store, if any.
        /// </summary>
        /// <value><see langword="default"/> if there is no state, e.g. when the <see cref="ChatMessageStore"/> is first created.</value>
        public JsonElement SerializedState { get; set; }

        /// <summary>
        /// Gets or sets the JSON serialization options to use when deserializing the <see cref="SerializedState"/>.
        /// </summary>
        public JsonSerializerOptions? JsonSerializerOptions { get; set; }
    }
}
