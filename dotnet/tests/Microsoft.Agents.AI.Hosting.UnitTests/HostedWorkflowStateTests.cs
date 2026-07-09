// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Moq;

namespace Microsoft.Agents.AI.Hosting.UnitTests;

/// <summary>
/// Unit tests for the <see cref="HostedWorkflowState"/> class.
/// </summary>
public class HostedWorkflowStateTests
{
    [Fact]
    public void Constructor_NullWorkflow_Throws() =>
        // Act & Assert
        Assert.Throws<ArgumentNullException>("workflow", () => new HostedWorkflowState(null!));

    [Fact]
    public void Workflow_ReturnsProvidedWorkflow()
    {
        // Arrange
        var workflow = CreateTestWorkflow();

        // Act
        var state = new HostedWorkflowState(workflow);

        // Assert
        Assert.Same(workflow, state.Workflow);
    }

    [Fact]
    public void TryGetCheckpoint_UnknownSession_ReturnsFalse()
    {
        // Arrange
        var state = new HostedWorkflowState(CreateTestWorkflow());

        // Act
        bool found = state.TryGetCheckpoint("unknown", out var checkpoint);

        // Assert
        Assert.False(found);
        Assert.Null(checkpoint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RunOrResumeAsync_InvalidSessionId_ThrowsAsync(string? sessionId)
    {
        // Arrange
        var state = new HostedWorkflowState(CreateTestWorkflow());

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(() => state.RunOrResumeAsync(sessionId!, "input").AsTask());
    }

    [Fact]
    public async Task RunOrResumeAsync_NullInput_ThrowsAsync()
    {
        // Arrange
        var state = new HostedWorkflowState(CreateTestWorkflow());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>("input", () => state.RunOrResumeAsync<string>("s1", null!).AsTask());
    }

    [Fact]
    public async Task RunOrResumeAsync_FirstTurn_RunsAndRecordsCheckpointAsync()
    {
        // Arrange
        var state = new HostedWorkflowState(CreateEchoWorkflow());

        // Act
        HostedWorkflowRunResult result = await state.RunOrResumeAsync("s1", InputMessages("hello"));

        // Assert
        Assert.NotEmpty(result.Events);
        Assert.NotNull(result.Checkpoint);
        Assert.True(state.TryGetCheckpoint("s1", out CheckpointInfo? checkpoint));
        Assert.Same(result.Checkpoint, checkpoint);
        Assert.Contains("hello", OutputText(result));
    }

    [Fact]
    public async Task RunOrResumeAsync_SecondTurn_ResumesWithNewInputAndCompletesAsync()
    {
        // Arrange
        var state = new HostedWorkflowState(CreateEchoWorkflow());
        HostedWorkflowRunResult first = await state.RunOrResumeAsync("s1", InputMessages("hello"));
        CheckpointInfo? firstCheckpoint = first.Checkpoint;

        // Act: the second turn must restore the checkpoint and run forward with the NEW input.
        // A regression here (resuming with no input) would hang, so guard with a timeout.
        HostedWorkflowRunResult second = await state.RunOrResumeAsync("s1", InputMessages("world"))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30));

        // Assert: the resumed turn processed the new input and advanced the checkpoint.
        Assert.NotEmpty(second.Events);
        Assert.Contains("world", OutputText(second));
        Assert.NotNull(second.Checkpoint);
        Assert.NotSame(firstCheckpoint, second.Checkpoint);
        Assert.True(state.TryGetCheckpoint("s1", out CheckpointInfo? head));
        Assert.Same(second.Checkpoint, head);
    }

    private static List<ChatMessage> InputMessages(string text) => [new(ChatRole.User, text)];

    private static string OutputText(HostedWorkflowRunResult result) =>
        string.Concat(
            result.Events
                .OfType<WorkflowOutputEvent>()
                .Select(e => e.Data)
                .OfType<IEnumerable<ChatMessage>>()
                .SelectMany(messages => messages)
                .Select(m => m.Text));

    private static Workflow CreateEchoWorkflow() =>
        AgentWorkflowBuilder.BuildSequential(workflowName: "echo", agents: [new TestEchoAgent(name: "echo")]);

    private static Workflow CreateTestWorkflow()
    {
        var mockAgent = new Mock<AIAgent>();
        mockAgent.Setup(a => a.Name).Returns("testAgent");
        return AgentWorkflowBuilder.BuildSequential(workflowName: "wf", agents: [mockAgent.Object]);
    }
}
