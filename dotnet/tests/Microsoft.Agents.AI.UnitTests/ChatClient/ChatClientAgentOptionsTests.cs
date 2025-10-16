// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Moq;

namespace Microsoft.Agents.AI.UnitTests;

/// <summary>
/// Unit tests for the <see cref="ChatClientAgentOptions"/> class.
/// </summary>
public class ChatClientAgentOptionsTests
{
    [Fact]
    public void DefaultConstructor_InitializesWithNullValues()
    {
        // Act
        var options = new ChatClientAgentOptions();

        // Assert
        Assert.Null(options.Name);
        Assert.Null(options.Instructions);
        Assert.Null(options.Description);
        Assert.Null(options.ChatOptions);
        Assert.Null(options.ChatMessageStoreFactory);
        Assert.Null(options.AIContextProviderFactory);
    }

    [Fact]
    public void ParameterizedConstructor_WithNullValues_SetsPropertiesCorrectly()
    {
        // Act
        var options = new ChatClientAgentOptions() { Instructions = null, Name = null, Description = null, ChatOptions = new() { Tools = null } };

        // Assert
        Assert.Null(options.Name);
        Assert.Null(options.Instructions);
        Assert.Null(options.Description);
        Assert.Null(options.AIContextProviderFactory);
    }

    [Fact]
    public void ParameterizedConstructor_WithInstructionsOnly_SetsChatOptionsWithInstructions()
    {
        // Arrange
        const string Instructions = "Test instructions";

        // Act
        var options = new ChatClientAgentOptions()
        {
            Instructions = Instructions,
            Name = null,
            Description = null,
            ChatOptions = new() { Tools = null }
        };

        // Assert
        Assert.Null(options.Name);
        Assert.Equal(Instructions, options.Instructions);
        Assert.Null(options.Description);
    }

    [Fact]
    public void ParameterizedConstructor_WithToolsOnly_SetsChatOptionsWithTools()
    {
        // Arrange
        var tools = new List<AITool> { AIFunctionFactory.Create(() => "test") };

        // Act
        var options = new ChatClientAgentOptions()
        {
            Instructions = null,
            Name = null,
            Description = null,
            ChatOptions = new() { Tools = tools }
        };

        // Assert
        Assert.Null(options.Name);
        Assert.Null(options.Instructions);
        Assert.Equal(options.Instructions, options.ChatOptions.Instructions);
        Assert.Null(options.Description);
        Assert.NotNull(options.ChatOptions);
        AssertSameTools(tools, options.ChatOptions.Tools);
    }

    [Fact]
    public void ParameterizedConstructor_WithInstructionsAndTools_SetsChatOptionsWithBoth()
    {
        // Arrange
        const string Instructions = "Test instructions";
        var tools = new List<AITool> { AIFunctionFactory.Create(() => "test") };

        // Act
        var options = new ChatClientAgentOptions()
        {
            Instructions = Instructions,
            Name = null,
            Description = null,
            ChatOptions = new() { Tools = tools }
        };

        // Assert
        Assert.Null(options.Name);
        Assert.Equal(Instructions, options.Instructions);
        Assert.Equal(Instructions, options.ChatOptions.Instructions);
        Assert.Null(options.Description);
        Assert.NotNull(options.ChatOptions);
        AssertSameTools(tools, options.ChatOptions.Tools);
    }

    [Fact]
    public void ParameterizedConstructor_WithAllParameters_SetsAllPropertiesCorrectly()
    {
        // Arrange
        const string Instructions = "Test instructions";
        const string Name = "Test name";
        const string Description = "Test description";
        var tools = new List<AITool> { AIFunctionFactory.Create(() => "test") };

        // Act
        var options = new ChatClientAgentOptions()
        {
            Instructions = Instructions,
            Name = Name,
            Description = Description,
            ChatOptions = new() { Tools = tools }
        };

        // Assert
        Assert.Equal(Name, options.Name);
        Assert.Equal(Instructions, options.Instructions);
        Assert.Equal(Instructions, options.ChatOptions.Instructions);
        Assert.Equal(Description, options.Description);
        Assert.NotNull(options.ChatOptions);
        AssertSameTools(tools, options.ChatOptions.Tools);
    }

    [Fact]
    public void ParameterizedConstructor_WithNameAndDescriptionOnly_DoesNotCreateChatOptions()
    {
        // Arrange
        const string Name = "Test name";
        const string Description = "Test description";

        // Act
        var options = new ChatClientAgentOptions()
        {
            Instructions = null,
            Name = Name,
            Description = Description,
            ChatOptions = new() { Tools = null },
        };

        // Assert
        Assert.Equal(Name, options.Name);
        Assert.Null(options.Instructions);
        Assert.Equal(Description, options.Description);
    }

    [Fact]
    public void Clone_CreatesDeepCopyWithSameValues()
    {
        // Arrange
        const string Instructions = "Test instructions";
        const string Name = "Test name";
        const string Description = "Test description";
        var tools = new List<AITool> { AIFunctionFactory.Create(() => "test") };

        static ChatMessageStore ChatMessageStoreFactory(
            ChatClientAgentOptions.ChatMessageStoreFactoryContext ctx) => new Mock<ChatMessageStore>().Object;

        static AIContextProvider AIContextProviderFactory(
            ChatClientAgentOptions.AIContextProviderFactoryContext ctx) =>
            new Mock<AIContextProvider>().Object;

        var original = new ChatClientAgentOptions()
        {
            Instructions = Instructions,
            Name = Name,
            Description = Description,
            ChatOptions = new() { Tools = tools },
            Id = "test-id",
            ChatMessageStoreFactory = ChatMessageStoreFactory,
            AIContextProviderFactory = AIContextProviderFactory
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original.Id, clone.Id);
        Assert.Equal(original.Name, clone.Name);
        Assert.Equal(original.Instructions, clone.Instructions);
        Assert.Equal(original.Description, clone.Description);
        Assert.Same(original.ChatMessageStoreFactory, clone.ChatMessageStoreFactory);
        Assert.Same(original.AIContextProviderFactory, clone.AIContextProviderFactory);

        // ChatOptions should be cloned, not the same reference
        Assert.NotSame(original.ChatOptions, clone.ChatOptions);
        Assert.Equal(original.ChatOptions?.Instructions, clone.ChatOptions?.Instructions);
        Assert.Equal(original.ChatOptions?.Tools, clone.ChatOptions?.Tools);
    }

    [Fact]
    public void Clone_WithoutProvidingChatOptions_ClonesCorrectly()
    {
        // Arrange
        var original = new ChatClientAgentOptions
        {
            Id = "test-id",
            Name = "Test name",
            Instructions = "Test instructions",
            Description = "Test description"
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original.Id, clone.Id);
        Assert.Equal(original.Name, clone.Name);
        Assert.Equal(original.Instructions, clone.Instructions);
        Assert.Equal(original.Description, clone.Description);
        Assert.Equal(original.ChatOptions?.Instructions, clone.ChatOptions?.Instructions);
        Assert.Null(clone.ChatMessageStoreFactory);
        Assert.Null(clone.AIContextProviderFactory);
    }

    private static void AssertSameTools(IList<AITool>? expected, IList<AITool>? actual)
    {
        var index = 0;
        foreach (var tool in expected ?? [])
        {
            Assert.Same(tool, actual?[index]);
            index++;
        }
    }
}
