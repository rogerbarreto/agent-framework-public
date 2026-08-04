// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.AgentServer.Responses;
using Azure.AI.AgentServer.Responses.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MeaiTextContent = Microsoft.Extensions.AI.TextContent;

namespace Microsoft.Agents.AI.Foundry.Hosting.UnitTests;

public class AgentFrameworkResponseHandlerTests
{
    [Fact]
    public async Task CreateAsync_WithDefaultAgent_ProducesStreamEventsAsync()
    {
        // Arrange
        var agent = CreateTestAgent("Hello from the agent!");
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton<AIAgent>(agent);
        services.AddSingleton<ILogger<AgentFrameworkResponseHandler>>(NullLogger<AgentFrameworkResponseHandler>.Instance);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test" };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        Assert.True(events.Count >= 4, $"Expected at least 4 events, got {events.Count}");
        Assert.IsType<ResponseCreatedEvent>(events[0]);
        Assert.IsType<ResponseInProgressEvent>(events[1]);
    }

    [Fact]
    public async Task CreateAsync_WithKeyedAgent_ResolvesCorrectAgentAsync()
    {
        // Arrange
        var agent = CreateTestAgent("Keyed agent response");
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddKeyedSingleton<AIAgent>("my-agent", agent);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test", AgentReference = new AgentReference("my-agent") };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert - should have produced events from the keyed agent
        Assert.True(events.Count >= 4);
        Assert.IsType<ResponseCreatedEvent>(events[0]);
    }

    [Fact]
    public async Task CreateAsync_NoAgentRegistered_ThrowsInvalidOperationExceptionAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test" };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public void Constructor_NullServiceProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AgentFrameworkResponseHandler(null!, NullLogger<AgentFrameworkResponseHandler>.Instance));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        Assert.Throws<ArgumentNullException>(
            () => new AgentFrameworkResponseHandler(sp, null!));
    }

    [Fact]
    public async Task CreateAsync_ResolvesAgentByModelFieldAsync()
    {
        // Arrange
        var agent = CreateTestAgent("model agent");
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddKeyedSingleton<AIAgent>("my-agent", agent);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "my-agent" };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        Assert.True(events.Count >= 4);
        Assert.IsType<ResponseCreatedEvent>(events[0]);
    }

    [Fact]
    public async Task CreateAsync_ResolvesAgentByEntityIdMetadataAsync()
    {
        // Arrange
        var agent = CreateTestAgent("entity agent");
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddKeyedSingleton<AIAgent>("entity-agent", agent);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "" };
        var metadata = new Metadata();
        metadata.AdditionalProperties["entity_id"] = "entity-agent";
        request.Metadata = metadata;
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        Assert.True(events.Count >= 4);
        Assert.IsType<ResponseCreatedEvent>(events[0]);
    }

    [Fact]
    public async Task CreateAsync_NamedAgentNotFound_FallsBackToDefaultAsync()
    {
        // Arrange
        var agent = CreateTestAgent("default agent");
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton<AIAgent>(agent);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test", AgentReference = new AgentReference("nonexistent-agent") };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        Assert.True(events.Count >= 4);
        Assert.IsType<ResponseCreatedEvent>(events[0]);
    }

    [Fact]
    public async Task CreateAsync_NoAgentFound_ErrorMessageIncludesAgentNameAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test", AgentReference = new AgentReference("missing-agent") };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
            {
            }
        });

        Assert.Contains("missing-agent", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_NoAgentNoName_ErrorMessageIsGenericAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "" };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
            {
            }
        });

        Assert.Contains("No agent name specified", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_AgentResolvedBeforeEmitCreated_ExceptionHasNoEventsAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test" };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act
        var events = new List<ResponseStreamEvent>();
        bool threw = false;
        try
        {
            await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
            {
                events.Add(evt);
            }
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        // Assert
        Assert.True(threw);
        Assert.Empty(events);
    }

    [Fact]
    public async Task CreateAsync_WithHistory_PrependsHistoryToMessagesAsync()
    {
        // Arrange
        var agent = new CapturingAgent();
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton<AIAgent>(agent);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test" };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var historyItem = new OutputItemMessage(
            id: "hist_1",
            role: MessageRole.Assistant,
            content: [new MessageContentOutputTextContent(
                "Previous response",
                Array.Empty<Annotation>(),
                Array.Empty<LogProb>())],
            status: MessageStatus.Completed);

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutputItem[] { historyItem });
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        Assert.NotNull(agent.CapturedMessages);
        var messages = agent.CapturedMessages.ToList();
        Assert.True(messages.Count >= 2);
        Assert.Equal(ChatRole.Assistant, messages[0].Role);
    }

    [Fact]
    public async Task CreateAsync_WithInputItems_UsesResolvedInputItemsAsync()
    {
        // Arrange
        var agent = new CapturingAgent();
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton<AIAgent>(agent);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test" };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Raw input" } } }
        });

        var inputItem = new ItemMessage(
            MessageRole.Assistant,
            [new MessageContentInputTextContent("Resolved input")]);

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Item[] { inputItem });

        // Act
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        Assert.NotNull(agent.CapturedMessages);
        var messages = agent.CapturedMessages.ToList();
        Assert.Single(messages);
        Assert.Equal(ChatRole.Assistant, messages[0].Role);
    }

    [Fact]
    public async Task CreateAsync_NoInputItems_FallsBackToRawRequestInputAsync()
    {
        // Arrange
        var agent = new CapturingAgent();
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton<AIAgent>(agent);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test" };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Raw input" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        Assert.NotNull(agent.CapturedMessages);
        var messages = agent.CapturedMessages.ToList();
        Assert.Single(messages);
        Assert.Equal(ChatRole.User, messages[0].Role);
    }

    [Fact]
    public async Task CreateAsync_PassesInstructionsToAgentAsync()
    {
        // Arrange
        var agent = new CapturingAgent();
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton<AIAgent>(agent);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse
        {
            Model = "test",
            Instructions = "You are a helpful assistant.",
        };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        Assert.NotNull(agent.CapturedOptions);
        var chatClientOptions = Assert.IsType<ChatClientAgentRunOptions>(agent.CapturedOptions);
        Assert.Equal("You are a helpful assistant.", chatClientOptions.ChatOptions?.Instructions);
    }

    [Fact]
    public async Task CreateAsync_AgentThrows_EmitsFailedEventWithErrorMessageAsync()
    {
        // Arrange
        var agent = new ThrowingAgent(new InvalidOperationException("Agent crashed"));
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton<AIAgent>(agent);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test" };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act — collect all events
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert — should contain created, in_progress, and failed (with real error message)
        Assert.Contains(events, e => e is ResponseCreatedEvent);
        Assert.Contains(events, e => e is ResponseInProgressEvent);
        var failedEvent = Assert.Single(events.OfType<ResponseFailedEvent>());
        Assert.Contains("Agent crashed", failedEvent.Response.Error.Message);
    }

    [Fact]
    public async Task CreateAsync_MultipleKeyedAgents_ResolvesCorrectOneAsync()
    {
        // Arrange
        var agent1 = CreateTestAgent("Agent 1 response");
        var agent2 = CreateTestAgent("Agent 2 response");
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddKeyedSingleton<AIAgent>("agent-1", agent1);
        services.AddKeyedSingleton<AIAgent>("agent-2", agent2);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test", AgentReference = new AgentReference("agent-2") };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        Assert.True(events.Count >= 4);
        Assert.IsType<ResponseCreatedEvent>(events[0]);
    }

    [Fact]
    public async Task CreateAsync_CancellationDuringExecution_PropagatesOperationCanceledExceptionAsync()
    {
        // Arrange
        var agent = new CancellationCheckingAgent();
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton<AIAgent>(agent);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test" };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in handler.CreateAsync(request, mockContext.Object, cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task CreateAsync_DefaultAgent_IsAutoWrappedWithOpenTelemetryAsync()
    {
        // Arrange — register a plain (non-instrumented) agent
        var agent = CreateTestAgent("otel test response");
        var services = new ServiceCollection();
        services.AddSingleton<AgentSessionStore>(new InMemoryAgentSessionStore());
        services.AddSingleton<AIAgent>(agent);
        services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        var sp = services.BuildServiceProvider();

        var handler = new AgentFrameworkResponseHandler(sp, NullLogger<AgentFrameworkResponseHandler>.Instance);

        var request = new CreateResponse { Model = "test" };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var mockContext = new Mock<ResponseContext>("resp_" + new string('0', 46)) { CallBase = true };
        mockContext.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OutputItem>());
        mockContext.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Item>());

        // Act — OTel wrapping must not break the stream
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, mockContext.Object, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert — stream events are still produced correctly through the wrapper
        Assert.True(events.Count >= 4, $"Expected at least 4 events, got {events.Count}");
        Assert.IsType<ResponseCreatedEvent>(events[0]);
        Assert.IsType<ResponseInProgressEvent>(events[1]);
    }

    #region Resume detection

    [Fact]
    public async Task CreateAsync_FirstTurnOfAKnownConversation_StillReceivesTheServiceHistoryAsync()
    {
        // Arrange: the first turn this container serves for a conversation the service already holds
        // history for. Nothing has been persisted for it yet, so this is not a resume: the history has
        // to be handed to the agent, otherwise it answers knowing nothing of the conversation.
        var agent = new CapturingAgent();
        var handler = BuildHandlerWith(agent, new FakeHostedSessionIsolationKeyProvider(), new InMemoryAgentSessionStore());
        var request = new CreateResponse { Model = "test" };
        request.Conversation = BinaryData.FromString("\"conv-known\"");
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "new question" } } }
        });
        var ctx = new Mock<ResponseContext>("resp_" + new string('4', 46)) { CallBase = true };
        ctx.Setup(x => x.PlatformContext).Returns(new PlatformContext("alice", null));
        ctx.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync([NewHistoryMessageItem("msg_hist_1", "earlier turn")]);
        ctx.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Item>());

        // Act
        await DrainEventsAsync(handler.CreateAsync(request, ctx.Object, CancellationToken.None));

        // Assert: whether this is a resume is answered by the session store, not by looking for state on
        // the session. The handler writes the caller's identity onto a session before this point, so a
        // freshly created session already carries state and reading that as "it has run before" made the
        // first turn of every conversation look like a resume, dropping its history. It only showed up
        // when hosted, because there is no identity to write locally.
        Assert.NotNull(agent.CapturedMessages);
        Assert.Contains(agent.CapturedMessages!, m => m.Text.Contains("earlier turn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_SecondTurnOfAConversation_DoesNotReplayTheServiceHistoryAsync()
    {
        // Arrange: a first turn that persists a session for the conversation.
        const string ConversationId = "conv-resumed";
        var agent = new CapturingAgent();
        var store = new InMemoryAgentSessionStore();
        var handler = BuildHandlerWith(agent, new FakeHostedSessionIsolationKeyProvider(), store);
        await DrainEventsAsync(handler.CreateAsync(
            NewConversationTurn(ConversationId, "first question"),
            NewServingContext("resp_" + new string('5', 46), []),
            CancellationToken.None));

        // Act: a second turn of the same conversation, for which the service now reports history.
        await DrainEventsAsync(handler.CreateAsync(
            NewConversationTurn(ConversationId, "second question"),
            NewServingContext("resp_" + new string('6', 46), [NewHistoryMessageItem("msg_hist_1", "first question")]),
            CancellationToken.None));

        // Assert: a session was stored by the first turn, so this one resumes it. Replaying the history
        // here would re-drive work the session already carries, which is what breaks a workflow that was
        // paused waiting for input.
        Assert.NotNull(agent.CapturedMessages);
        Assert.DoesNotContain(agent.CapturedMessages!, m => m.Text.Contains("first question", StringComparison.Ordinal));
    }

    private static CreateResponse NewConversationTurn(string conversationId, string text)
    {
        var request = new CreateResponse { Model = "test" };
        request.Conversation = BinaryData.FromString($"\"{conversationId}\"");
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_" + Guid.NewGuid().ToString("N")[..8], status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text } } }
        });
        return request;
    }

    private static ResponseContext NewServingContext(string responseId, IReadOnlyList<OutputItem> history)
    {
        var ctx = new Mock<ResponseContext>(responseId) { CallBase = true };
        ctx.Setup(x => x.PlatformContext).Returns(new PlatformContext("alice", null));
        ctx.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(history);
        ctx.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Item>());
        return ctx.Object;
    }

    #endregion

    #region Chat history source routing

    // These tests pin down who supplies the conversation history to a hosted agent. Three of them are
    // regression tests for the behaviour this region replaced: the handler used to fetch the platform
    // history and prepend it to the input of every turn, while a ChatClientAgent independently ran its
    // own ChatHistoryProvider. Against that older handler these three fail:
    //   - DoesNotCopyPlatformHistoryIntoTheSession        (the service's turns ended up in the session)
    //   - DoesNotAskItToStorePlatformHistory              (and in a custom provider's own database)
    //   - UsesThatProviderInsteadOfThePlatform            (both sources reached the model at once)

    [Fact]
    public async Task CreateAsync_AgentWithoutProviderPipeline_ReceivesPlatformHistoryInInputAsync()
    {
        // Arrange: a plain AIAgent (a hosted workflow, for example) has no ChatHistoryProvider
        // pipeline, so the handler is the only thing that can hand it the platform history.
        var agent = new CapturingAgent();
        var handler = BuildHandlerWith(agent, new FakeHostedSessionIsolationKeyProvider(), new InMemoryAgentSessionStore());
        var (request, ctx) = BuildChainRequest("resp_" + new string('1', 46), callId: null);
        ctx.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync([NewHistoryMessageItem("msg_hist_1", "earlier turn")]);

        // Act
        await DrainEventsAsync(handler.CreateAsync(request, ctx.Object, CancellationToken.None));

        // Assert
        Assert.NotNull(agent.CapturedMessages);
        Assert.Contains(agent.CapturedMessages!, m => m.Text.Contains("earlier turn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_ChatClientAgentWithoutHistoryProvider_SendsPlatformHistoryExactlyOnceAsync()
    {
        // Arrange: no chat history provider was supplied, so the platform stays the source and the
        // handler registers FoundryChatHistoryProvider for the turn.
        var captured = new List<ChatMessage>();
        var agent = new ChatClientAgent(CreateCapturingChatClient(captured));
        var handler = BuildHandlerWith(agent, new FakeHostedSessionIsolationKeyProvider(), new InMemoryAgentSessionStore());
        var (request, ctx) = BuildChainRequest("resp_" + new string('2', 46), callId: null);
        ctx.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync([NewHistoryMessageItem("msg_hist_1", "earlier turn")]);

        // Act
        await DrainEventsAsync(handler.CreateAsync(request, ctx.Object, CancellationToken.None));

        // Assert: the earlier turn still reaches the model, and only one copy of it does.
        Assert.Single(captured, m => m.Text.Contains("earlier turn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_ChatClientAgentWithHistoryProvider_DoesNotAskItToStorePlatformHistoryAsync()
    {
        // Arrange: an agent whose own provider records everything it is asked to store, and a platform
        // that already holds an earlier turn of this conversation.
        var recordingProvider = new RecordingChatHistoryProvider();
        var agent = new ChatClientAgent(
            CreateCapturingChatClient([]),
            new ChatClientAgentOptions { ChatHistoryProvider = recordingProvider });
        var handler = BuildHandlerWith(agent, new FakeHostedSessionIsolationKeyProvider(), new InMemoryAgentSessionStore());
        var (request, ctx) = BuildChainRequest("resp_" + new string('5', 46), callId: null);
        ctx.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync([NewHistoryMessageItem("msg_hist_1", "already kept by the service")]);

        // Act
        await DrainEventsAsync(handler.CreateAsync(request, ctx.Object, CancellationToken.None));

        // Assert: the agent's own store must not be told to write a turn the service already holds. The
        // older handler passed that turn in as ordinary input, and since platform items carry no
        // chat-history source marker the provider took it for newly written content and stored it,
        // duplicating into the agent's own database a conversation the service was already keeping.
        Assert.DoesNotContain(recordingProvider.Stored, m => m.Text.Contains("already kept by the service", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_ChatClientAgentWithoutHistoryProvider_DoesNotCopyPlatformHistoryIntoTheSessionAsync()
    {
        // Arrange: the platform reports one earlier turn for this conversation.
        const string ResponseId = "resp_" + "4444444444444444444444444444444444444444444444";
        var store = new InMemoryAgentSessionStore();
        var agent = new ChatClientAgent(CreateCapturingChatClient([]));
        var handler = BuildHandlerWith(agent, new FakeHostedSessionIsolationKeyProvider(), store);
        var (request, ctx) = BuildChainRequest(ResponseId, callId: null);
        ctx.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync([NewHistoryMessageItem("msg_hist_1", "already kept by the service")]);

        // Act
        await DrainEventsAsync(handler.CreateAsync(request, ctx.Object, CancellationToken.None));

        // Assert: the turn the service already keeps must not be written into the persisted session as
        // well. The older handler fed that history to the agent as ordinary input, and because platform
        // items carry no chat-history source marker the agent's default in-memory provider stored it as
        // if this turn had produced it, leaving a second copy on disk that then drifts from the service.
        Assert.DoesNotContain("already kept by the service", await SerializedSessionOfAsync(agent, store, ResponseId), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_ChatClientAgentWithHistoryProvider_UsesThatProviderInsteadOfThePlatformAsync()
    {
        // Arrange: the agent was created with its own chat history provider, so that provider owns the
        // conversation and the platform history must not be used at all.
        var captured = new List<ChatMessage>();
        var agent = new ChatClientAgent(
            CreateCapturingChatClient(captured),
            new ChatClientAgentOptions { ChatHistoryProvider = new FixedChatHistoryProvider("from my own store") });
        var handler = BuildHandlerWith(agent, new FakeHostedSessionIsolationKeyProvider(), new InMemoryAgentSessionStore());
        var (request, ctx) = BuildChainRequest("resp_" + new string('3', 46), callId: null);
        ctx.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync([NewHistoryMessageItem("msg_hist_1", "from the platform")]);

        // Act
        await DrainEventsAsync(handler.CreateAsync(request, ctx.Object, CancellationToken.None));

        // Assert: one source only. The older handler sent both, so the model received the agent's own
        // history and the platform's interleaved as a single conversation.
        Assert.Contains(captured, m => m.Text.Contains("from my own store", StringComparison.Ordinal));
        Assert.DoesNotContain(captured, m => m.Text.Contains("from the platform", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_ChatClientAgentWithoutHistoryProvider_KeepsContainerSideMemoryWhenTheRequestIsNotStoredAsync()
    {
        // Arrange: two turns of one conversation, and a service that holds no history for it. That is
        // what a store=false request looks like: nothing was persisted, so GetHistoryItemIdsAsync finds
        // no record and the service can serve nothing back.
        var captured = new List<ChatMessage>();
        var store = new InMemoryAgentSessionStore();
        var agent = new ChatClientAgent(CreateCapturingChatClient(captured));
        var handler = BuildHandlerWith(agent, new FakeHostedSessionIsolationKeyProvider(), store);

        await DrainEventsAsync(handler.CreateAsync(
            NewUnstoredRequest("conv-unstored", "first question"),
            NewEmptyHistoryContext("resp_" + new string('6', 46)),
            CancellationToken.None));
        captured.Clear();

        // Act: a second turn of the same conversation.
        await DrainEventsAsync(handler.CreateAsync(
            NewUnstoredRequest("conv-unstored", "second question"),
            NewEmptyHistoryContext("resp_" + new string('7', 46)),
            CancellationToken.None));

        // Assert: the agent still remembers the first turn. When the service stores nothing there is no
        // second copy to worry about, and the container's own session is the only memory the
        // conversation can have, so the default in-memory provider must keep carrying it.
        Assert.Contains(captured, m => m.Text.Contains("first question", StringComparison.Ordinal));
    }

    private static CreateResponse NewUnstoredRequest(string conversationId, string text)
    {
        var request = new CreateResponse { Model = "test", Store = false };
        request.Conversation = BinaryData.FromString($"\"{conversationId}\"");
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_" + Guid.NewGuid().ToString("N")[..8], status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text } } }
        });
        return request;
    }

    private static ResponseContext NewEmptyHistoryContext(string responseId)
    {
        var ctx = new Mock<ResponseContext>(responseId) { CallBase = true };
        ctx.Setup(x => x.PlatformContext).Returns(new PlatformContext("alice", null));
        ctx.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<OutputItem>());
        ctx.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Item>());
        return ctx.Object;
    }

    [Fact]
    public async Task CreateAsync_ChatClientAgentWithoutHistoryProvider_KeepsMixedStoredAndUnstoredConversationWholeAsync()
    {
        // Arrange: one conversation whose first turn is stored by the service and whose later turns are
        // not. From turn 2 on, the service keeps serving turn 1 and nothing else, because it was never
        // asked to store the rest.
        var captured = new List<ChatMessage>();
        var agent = new ChatClientAgent(CreateCapturingChatClient(captured));
        var handler = BuildHandlerWith(agent, new FakeHostedSessionIsolationKeyProvider(), new InMemoryAgentSessionStore());
        OutputItem[] servedByTheService =
        [
            NewHistoryMessageItem("msg_hist_1", "first question"),
            NewHistoryMessageItem("msg_hist_2", "first answer"),
        ];

        // Turn 1 is stored, so the service records it.
        await DrainEventsAsync(handler.CreateAsync(
            NewConversationRequest("conv-mixed", "first question", store: true),
            NewContextServing("resp_" + new string('6', 46), []),
            CancellationToken.None));

        // Turn 2 is not stored: the service still serves turn 1, and this turn is only kept in the session.
        await DrainEventsAsync(handler.CreateAsync(
            NewConversationRequest("conv-mixed", "second question", store: false),
            NewContextServing("resp_" + new string('7', 46), servedByTheService),
            CancellationToken.None));
        captured.Clear();

        // Act: a third turn, also not stored.
        await DrainEventsAsync(handler.CreateAsync(
            NewConversationRequest("conv-mixed", "third question", store: false),
            NewContextServing("resp_" + new string('8', 46), servedByTheService),
            CancellationToken.None));

        // Assert: the conversation is whole and each turn appears once. The stored turn comes from the
        // service and the unstored one from the session, and neither is counted twice.
        Assert.Single(captured, m => m.Text.Contains("first question", StringComparison.Ordinal));
        Assert.Single(captured, m => m.Text.Contains("second question", StringComparison.Ordinal));
        Assert.Single(captured, m => m.Text.Contains("third question", StringComparison.Ordinal));
    }

    private static CreateResponse NewConversationRequest(string conversationId, string text, bool store)
    {
        var request = new CreateResponse { Model = "test", Store = store };
        request.Conversation = BinaryData.FromString($"\"{conversationId}\"");
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_" + Guid.NewGuid().ToString("N")[..8], status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text } } }
        });
        return request;
    }

    private static ResponseContext NewContextServing(string responseId, IReadOnlyList<OutputItem> history)
    {
        var ctx = new Mock<ResponseContext>(responseId) { CallBase = true };
        ctx.Setup(x => x.PlatformContext).Returns(new PlatformContext("alice", null));
        ctx.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(history);
        ctx.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Item>());
        return ctx.Object;
    }

    /// <summary>Reads back the session the handler persisted for a response and returns it as JSON text.</summary>
    private static async Task<string> SerializedSessionOfAsync(AIAgent agent, InMemoryAgentSessionStore store, string responseId)
    {
        var sessionKey = HostedConversationKey.Resolve(conversationId: null, previousResponseId: null, responseId);
        var session = await store.GetSessionAsync(agent, sessionKey!, FakeHostedSessionIsolationKeyProvider.DefaultUserId, CancellationToken.None);

        // The handler persists the session at the end of every turn, so a missing one means the turn did
        // not get that far and the assertions below would otherwise pass without proving anything.
        Assert.NotNull(session);

        var serialized = await agent.SerializeSessionAsync(session, cancellationToken: CancellationToken.None);
        return serialized.GetRawText();
    }

    private static OutputItemMessage NewHistoryMessageItem(string id, string text) =>
        new(
            id: id,
            role: MessageRole.Assistant,
            content: [new MessageContentOutputTextContent(text, Array.Empty<Annotation>(), Array.Empty<LogProb>())],
            status: MessageStatus.Completed);

    private static IChatClient CreateCapturingChatClient(List<ChatMessage> captured)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) =>
            {
                captured.AddRange(messages);
                return ToAsyncEnumerableUpdatesAsync(
                    new ChatResponseUpdate(ChatRole.Assistant, "ok") { MessageId = "resp_msg_1" });
            });
        return mock.Object;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ToAsyncEnumerableUpdatesAsync(params ChatResponseUpdate[] updates)
    {
        foreach (var update in updates)
        {
            yield return update;
        }

        await Task.CompletedTask;
    }

    /// <summary>A chat history provider that always returns the same message, standing in for one backed by a store.</summary>
    private sealed class FixedChatHistoryProvider(string text) : ChatHistoryProvider
    {
        protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(InvokingContext context, CancellationToken cancellationToken = default)
            => new([new ChatMessage(ChatRole.User, text)]);

        protected override ValueTask StoreChatHistoryAsync(InvokedContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>A chat history provider that records everything it is asked to write, standing in for one backed by a database.</summary>
    private sealed class RecordingChatHistoryProvider : ChatHistoryProvider
    {
        public List<ChatMessage> Stored { get; } = [];

        protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(InvokingContext context, CancellationToken cancellationToken = default)
            => new([]);

        protected override ValueTask StoreChatHistoryAsync(InvokedContext context, CancellationToken cancellationToken = default)
        {
            this.Stored.AddRange(context.RequestMessages);
            if (context.ResponseMessages is not null)
            {
                this.Stored.AddRange(context.ResponseMessages);
            }

            return default;
        }
    }

    #endregion

    private static TestAgent CreateTestAgent(string responseText)
    {
        return new TestAgent(responseText);
    }

    private static async IAsyncEnumerable<AgentResponseUpdate> ToAsyncEnumerableAsync(params AgentResponseUpdate[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private sealed class TestAgent(string responseText) : AIAgent
    {
        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            CancellationToken cancellationToken = default) =>
            ToAsyncEnumerableAsync(new AgentResponseUpdate
            {
                MessageId = "resp_msg_1",
                Contents = [new MeaiTextContent(responseText)]
            });

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(
            CancellationToken cancellationToken = default) =>
            new(new SimpleAgentSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default) =>
            new(JsonDocument.Parse("{}").RootElement);

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default) =>
            new(new SimpleAgentSession());
    }

    private sealed class ThrowingAgent(Exception exception) : AIAgent
    {
        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            CancellationToken cancellationToken = default) =>
            throw exception;

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(
            CancellationToken cancellationToken = default) =>
            new(new SimpleAgentSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default) =>
            new(JsonDocument.Parse("{}").RootElement);

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default) =>
            new(new SimpleAgentSession());
    }

    private sealed class CapturingAgent : AIAgent
    {
        public IEnumerable<ChatMessage>? CapturedMessages { get; private set; }
        public AgentRunOptions? CapturedOptions { get; private set; }

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            CancellationToken cancellationToken = default)
        {
            this.CapturedMessages = messages.ToList();
            this.CapturedOptions = options;
            return ToAsyncEnumerableAsync(new AgentResponseUpdate
            {
                MessageId = "resp_msg_1",
                Contents = [new MeaiTextContent("captured")]
            });
        }

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(
            CancellationToken cancellationToken = default) =>
            new(new SimpleAgentSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default) =>
            new(JsonDocument.Parse("{}").RootElement);

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default) =>
            new(new SimpleAgentSession());
    }

    private sealed class CancellationCheckingAgent : AIAgent
    {
        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new AgentResponseUpdate { Contents = [new MeaiTextContent("test")] };
            await Task.CompletedTask;
        }

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(
            CancellationToken cancellationToken = default) =>
            new(new SimpleAgentSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default) =>
            new(JsonDocument.Parse("{}").RootElement);

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions,
            CancellationToken cancellationToken = default) =>
            new(new SimpleAgentSession());
    }

    [Fact]
    public async Task CreateAsync_PreviousResponseIdChain_NoConversation_ReusesOneSessionAsync()
    {
        // Arrange
        var agent = new SessionCountingAgent();
        var fakeProvider = new FakeHostedSessionIsolationKeyProvider("alice");
        var store = new InMemoryAgentSessionStore();
        var handler = BuildHandlerWith(agent, fakeProvider, store);

        const string PartitionA = "aaaaaaaaaaaaaaaa00";
        var responseA = "caresp_" + PartitionA + new string('1', 32);
        var responseA2 = "caresp_" + PartitionA + new string('2', 32);

        // Turn 1: cold start, no conversation, no previous_response_id. Key from minted responseA.
        var (req1, ctx1) = BuildChainRequest(responseA, callId: null);
        await DrainEventsAsync(handler.CreateAsync(req1, ctx1.Object, CancellationToken.None));
        Assert.NotNull(agent.LastSession);

        // Turn 2: client echoes previous_response_id sharing the same partition; minted responseA2.
        var (req2, ctx2) = BuildChainRequest(responseA2, callId: null);
        req2.PreviousResponseId = responseA;
        agent.LastSession = null;
        await DrainEventsAsync(handler.CreateAsync(req2, ctx2.Object, CancellationToken.None));

        // Assert: both turns persisted under the same partition key → one created session.
        Assert.NotNull(agent.LastSession);
        Assert.Equal("alice", agent.LastSession!.GetHostedContext()!.UserId);
        Assert.Equal(1, agent.SessionCount);
    }

    [Fact]
    public async Task CreateAsync_SetsCallIdFromPlatformContext_VisibleDuringAgentRunAsync()
    {
        // Arrange
        var agent = new CallIdCapturingAgent();
        var handler = BuildHandlerWith(agent, new FakeHostedSessionIsolationKeyProvider("alice"), new InMemoryAgentSessionStore());
        var (request, ctx) = BuildChainRequest("caresp_" + new string('0', 50), callId: "call-xyz");

        // Act
        await DrainEventsAsync(handler.CreateAsync(request, ctx.Object, CancellationToken.None));

        // Assert: the call id observed inside the agent run (the same async flow that drives any
        // downstream MCP/tool egress) matches the platform-provided value. This guards against the
        // async-iterator AsyncLocal revert that would otherwise drop the call id before egress.
        Assert.Equal("call-xyz", agent.ObservedCallId);
    }

    [Fact]
    public async Task CreateAsync_NoCallIdInPlatformContext_LeavesAmbientNullAsync()
    {
        // Arrange
        var agent = new CallIdCapturingAgent();
        var handler = BuildHandlerWith(agent, new FakeHostedSessionIsolationKeyProvider("alice"), new InMemoryAgentSessionStore());
        var (request, ctx) = BuildChainRequest("caresp_" + new string('0', 50), callId: null);

        // Act
        await DrainEventsAsync(handler.CreateAsync(request, ctx.Object, CancellationToken.None));

        // Assert
        Assert.Null(agent.ObservedCallId);
    }

    [Fact]
    public async Task CreateAsync_AfterStreamCompletes_DoesNotLeakCallIdToCallerContextAsync()
    {
        // Arrange
        var agent = new CallIdCapturingAgent();
        var handler = BuildHandlerWith(agent, new FakeHostedSessionIsolationKeyProvider("alice"), new InMemoryAgentSessionStore());
        var (request, ctx) = BuildChainRequest("caresp_" + new string('0', 50), callId: "call-xyz");

        // The caller's ambient call id starts clear.
        Assert.Null(HostedCallContext.CallId);

        // Act
        await DrainEventsAsync(handler.CreateAsync(request, ctx.Object, CancellationToken.None));

        // Assert: HostedCallContext is documented request-scoped. The handler sets the AsyncLocal inside
        // its streaming iterator (observed by the agent run — see VisibleDuringAgentRun above), but that
        // write never escapes to the caller's execution context. After the stream completes the caller's
        // ambient call id is still null, so a stale call id cannot leak into a subsequent request that is
        // handled on the same thread.
        Assert.Equal("call-xyz", agent.ObservedCallId);
        Assert.Null(HostedCallContext.CallId);
    }

    // ── Multi-agent / multi-user file-system isolation (handler-driven, no live service) ─────────────
    // These drive the hosted-agent handler (the in-process "hosted instance") against a REAL
    // FileSystemAgentSessionStore and the REAL PlatformHostedSessionIsolationKeyProvider (no fake), so the
    // user id is genuinely captured from the request's x-agent-user-id (ResponseContext.PlatformContext).
    // They assert the on-disk layout {root}/a-{agent}/u-{userId}/c-{conv}.json for combinations of agent
    // name and user.

    [Fact]
    public async Task CreateAsync_MultipleUsersSameAgent_WritePerUserDirectoriesAsync()
    {
        var root = NewIsolationTempRoot();
        try
        {
            // Arrange: one store shared by the container, one agent ("concierge"), two users.
            var store = new FileSystemAgentSessionStore(root);
            var handler = BuildMultiAgentHandler(store, ("concierge", new RecordingAgent("concierge")));

            // Act: Alice and Bob each drive the same agent and the same conversation id.
            var (aliceReq, aliceCtx) = BuildUserRequest("concierge", "trip", userId: "alice");
            await DrainEventsAsync(handler.CreateAsync(aliceReq, aliceCtx.Object, CancellationToken.None));
            var (bobReq, bobCtx) = BuildUserRequest("concierge", "trip", userId: "bob");
            await DrainEventsAsync(handler.CreateAsync(bobReq, bobCtx.Object, CancellationToken.None));

            // Assert: each user's session is persisted under its own u-{userId} directory beneath the
            // shared a-{agent} directory; neither can reach the other's path.
            Assert.True(File.Exists(Path.Combine(store.RootDirectory, "a-concierge", "u-alice", "c-trip.json")));
            Assert.True(File.Exists(Path.Combine(store.RootDirectory, "a-concierge", "u-bob", "c-trip.json")));
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_MultipleAgentsSameUser_WritePerAgentDirectoriesAsync()
    {
        var root = NewIsolationTempRoot();
        try
        {
            // Arrange: one store shared by the container, two agents, one user.
            var store = new FileSystemAgentSessionStore(root);
            var handler = BuildMultiAgentHandler(store, ("concierge", new RecordingAgent("concierge")), ("scheduler", new RecordingAgent("scheduler")));

            // Act: the same user drives two different agents on the same conversation id.
            var (req1, ctx1) = BuildUserRequest("concierge", "trip", userId: "alice");
            await DrainEventsAsync(handler.CreateAsync(req1, ctx1.Object, CancellationToken.None));
            var (req2, ctx2) = BuildUserRequest("scheduler", "trip", userId: "alice");
            await DrainEventsAsync(handler.CreateAsync(req2, ctx2.Object, CancellationToken.None));

            // Assert: each agent buckets the user's session under its own a-{agent} directory, so two
            // agents in the same container cannot collide on a shared conversation id.
            Assert.True(File.Exists(Path.Combine(store.RootDirectory, "a-concierge", "u-alice", "c-trip.json")));
            Assert.True(File.Exists(Path.Combine(store.RootDirectory, "a-scheduler", "u-alice", "c-trip.json")));
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_SecondUserSameConversation_GetsFreshSessionNoLeakAsync()
    {
        var root = NewIsolationTempRoot();
        try
        {
            // Arrange.
            var store = new FileSystemAgentSessionStore(root);
            var agent = new RecordingAgent("concierge");
            var handler = BuildMultiAgentHandler(store, ("concierge", agent));

            // Act: Alice drives twice (cold start then resume), then Bob forges the same agent+conversation.
            var (a1Req, a1Ctx) = BuildUserRequest("concierge", "trip", userId: "alice");
            await DrainEventsAsync(handler.CreateAsync(a1Req, a1Ctx.Object, CancellationToken.None));
            var (a2Req, a2Ctx) = BuildUserRequest("concierge", "trip", userId: "alice");
            await DrainEventsAsync(handler.CreateAsync(a2Req, a2Ctx.Object, CancellationToken.None));
            var (bobReq, bobCtx) = BuildUserRequest("concierge", "trip", userId: "bob");
            await DrainEventsAsync(handler.CreateAsync(bobReq, bobCtx.Object, CancellationToken.None));

            // Assert: Alice's second turn restored her persisted session (a deserialize), while Bob's request
            // produced a freshly created session — Bob never deserialized Alice's state. Two creates total
            // (Alice turn 1, Bob turn 1) and one restore (Alice turn 2).
            Assert.Equal(2, agent.CreateCount);
            Assert.Equal(1, agent.DeserializeCount);
            // And the files live in distinct per-user directories.
            Assert.True(File.Exists(Path.Combine(store.RootDirectory, "a-concierge", "u-alice", "c-trip.json")));
            Assert.True(File.Exists(Path.Combine(store.RootDirectory, "a-concierge", "u-bob", "c-trip.json")));
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_NoUserIdCaptured_NotHosted_SucceedsUnscopedAsync()
    {
        var root = NewIsolationTempRoot();
        try
        {
            // Arrange: a non-hosted (local) request whose x-agent-user-id was not captured (PlatformContext
            // is null). Under unit tests FoundryEnvironment.IsHosted is false, so the container is treated as
            // local: per-user isolation is simply not triggered and the request succeeds instead of 500ing.
            // The session is persisted without a u-{userId} segment (unscoped). The hosted-but-missing-user
            // branch (which still rejects) cannot be unit-tested because FoundryEnvironment.IsHosted is a
            // process-cached static; it is exercised by the investigation repro app's "hosted" scenario.
            var store = new FileSystemAgentSessionStore(root);
            var handler = BuildMultiAgentHandler(store, ("concierge", new RecordingAgent("concierge")));
            var (req, ctx) = BuildUserRequest("concierge", "trip", userId: null);

            // Act: the request drains without throwing.
            await DrainEventsAsync(handler.CreateAsync(req, ctx.Object, CancellationToken.None));

            // Assert: the session is written under the agent bucket with NO per-user (u-*) segment.
            Assert.True(File.Exists(Path.Combine(store.RootDirectory, "a-concierge", "c-trip.json")));
            var agentDir = Path.Combine(store.RootDirectory, "a-concierge");
            Assert.Empty(Directory.GetDirectories(agentDir, "u-*"));
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    private static string NewIsolationTempRoot()
        => Path.Combine(Path.GetTempPath(), "handler-fs-isolation-" + Guid.NewGuid().ToString("N"));

    private static void CleanupTempRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static AgentFrameworkResponseHandler BuildMultiAgentHandler(AgentSessionStore store, params (string Name, AIAgent Agent)[] agents)
    {
        var services = new ServiceCollection();
        // Shared default store for the container; agents resolved by name via keyed DI. No isolation-key
        // provider is registered so the real PlatformHostedSessionIsolationKeyProvider reads x-agent-user-id.
        services.AddSingleton(store);
        foreach (var (name, agent) in agents)
        {
            services.AddKeyedSingleton(name, agent);
        }

        return new AgentFrameworkResponseHandler(services.BuildServiceProvider(), NullLogger<AgentFrameworkResponseHandler>.Instance);
    }

    private static (CreateResponse Request, Mock<ResponseContext> Context) BuildUserRequest(string agentName, string conversationId, string? userId)
    {
        // The agent is selected from the request's Model field (GetAgentName falls back to Model); the
        // conversation id pins the c-{contextId} leaf.
        var request = new CreateResponse { Model = agentName };
        request.Conversation = BinaryData.FromString($"\"{conversationId}\"");
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });

        var ctx = new Mock<ResponseContext>("caresp_" + new string('0', 50)) { CallBase = true };
        // x-agent-user-id captured -> PlatformContext carries the user id; not captured -> null PlatformContext.
        if (userId is null)
        {
            ctx.Setup(x => x.PlatformContext).Returns((PlatformContext)null!);
        }
        else
        {
            ctx.Setup(x => x.PlatformContext).Returns(new PlatformContext(userId, null));
        }
        ctx.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<OutputItem>());
        ctx.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Item>());
        return (request, ctx);
    }

    private static AgentFrameworkResponseHandler BuildHandlerWith(AIAgent agent, HostedSessionIsolationKeyProvider provider, AgentSessionStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(agent);
        services.AddSingleton(provider);
        return new AgentFrameworkResponseHandler(services.BuildServiceProvider(), NullLogger<AgentFrameworkResponseHandler>.Instance);
    }

    private static (CreateResponse Request, Mock<ResponseContext> Context) BuildChainRequest(string responseId, string? callId)
    {
        var request = new CreateResponse { Model = "test" };
        request.Input = BinaryData.FromObjectAsJson(new[]
        {
            new { type = "message", id = "msg_1", status = "completed", role = "user",
                  content = new[] { new { type = "input_text", text = "Hello" } } }
        });
        var ctx = new Mock<ResponseContext>(responseId) { CallBase = true };
        ctx.Setup(x => x.PlatformContext).Returns(new PlatformContext("alice", callId));
        ctx.Setup(x => x.GetHistoryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<OutputItem>());
        ctx.Setup(x => x.GetInputItemsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Item>());
        return (request, ctx);
    }

    private static async Task DrainEventsAsync(IAsyncEnumerable<ResponseStreamEvent> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    /// <summary>Stateful agent that counts created sessions and round-trips its <see cref="AgentSessionStateBag"/>.</summary>
    private sealed class SessionCountingAgent : AIAgent
    {
        public AgentSession? LastSession { get; set; }
        public int SessionCount { get; private set; }

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken = default)
        {
            this.LastSession = session;
            return ToAsyncEnumerableAsync(new AgentResponseUpdate { MessageId = "resp_msg_1", Contents = [new MeaiTextContent("ok")] });
        }

        protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        {
            this.SessionCount++;
            return new(new StatefulSession());
        }

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions, CancellationToken cancellationToken = default)
            => new(((StatefulSession)session).Serialize());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions, CancellationToken cancellationToken = default)
            => new(StatefulSession.Deserialize(serializedState));
    }

    /// <summary>Records how many sessions it created vs deserialized, to prove cross-user no-leak.</summary>
    private sealed class RecordingAgent : AIAgent
    {
        private readonly string? _name;

        public RecordingAgent(string? name = null)
        {
            this._name = name;
        }

        public override string? Name => this._name;

        public int CreateCount { get; private set; }
        public int DeserializeCount { get; private set; }

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken = default)
            => ToAsyncEnumerableAsync(new AgentResponseUpdate { MessageId = "resp_msg_1", Contents = [new MeaiTextContent("ok")] });

        protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        {
            this.CreateCount++;
            return new(new StatefulSession());
        }

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions, CancellationToken cancellationToken = default)
            => new(((StatefulSession)session).Serialize());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions, CancellationToken cancellationToken = default)
        {
            this.DeserializeCount++;
            return new(StatefulSession.Deserialize(serializedState));
        }
    }

    /// <summary>Reads <see cref="HostedCallContext.CallId"/> during its run, standing in for a downstream tool call.</summary>
    private sealed class CallIdCapturingAgent : AIAgent
    {
        public string? ObservedCallId { get; private set; }

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken = default)
        {
            this.ObservedCallId = HostedCallContext.CallId;
            return ToAsyncEnumerableAsync(new AgentResponseUpdate { MessageId = "resp_msg_1", Contents = [new MeaiTextContent("ok")] });
        }

        protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => new(new StatefulSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions, CancellationToken cancellationToken = default)
            => new(((StatefulSession)session).Serialize());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions, CancellationToken cancellationToken = default)
            => new(StatefulSession.Deserialize(serializedState));
    }

    private sealed class StatefulSession : AgentSession
    {
        public StatefulSession() { }
        private StatefulSession(AgentSessionStateBag bag) { this.StateBag = bag; }
        public JsonElement Serialize() => this.StateBag.Serialize();
        public static StatefulSession Deserialize(JsonElement e) => new(AgentSessionStateBag.Deserialize(e));
    }

    private sealed class SimpleAgentSession : AgentSession { }
}
