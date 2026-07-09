// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task RunOrResumeAsync_ResumeWithPendingRequest_DoesNotBlockAsync()
    {
        // Arrange: a human-in-the-loop workflow whose start executor forwards its input to a request port,
        // so the workflow emits a RequestInfoEvent and halts awaiting an external response.
        var state = new HostedWorkflowState(ApprovalGateWorkflow.Build());

        // First turn halts at the pending request (the non-blocking baseline).
        HostedWorkflowRunResult first = await state.RunOrResumeAsync("s1", "approve deploy")
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Contains(first.Events, e => e is RequestInfoEvent);
        Assert.NotNull(first.Checkpoint);

        // Act: resuming a workflow that halts at a pending request must also return instead of blocking
        // forever. A regression (blocking drain) hangs here, so guard with a timeout.
        HostedWorkflowRunResult second = await state.RunOrResumeAsync("s1", "approve deploy again")
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30));

        // Assert: the resumed turn surfaced the pending request and returned.
        Assert.Contains(second.Events, e => e is RequestInfoEvent);
    }

    [Fact]
    public async Task RunOrResumeAsync_ResumeMakesNoProgress_LogsWarningAsync()
    {
        // Arrange: a non-chat-protocol workflow that completes on the first turn.
        var loggerFactory = new CapturingLoggerFactory();
        var state = new HostedWorkflowState(StringEchoWorkflow.Build(), loggerFactory: loggerFactory);
        HostedWorkflowRunResult first = await state.RunOrResumeAsync("s1", "hello");
        Assert.NotEmpty(first.Events);
        Assert.NotNull(first.Checkpoint);

        // Act: resume with an input the start executor cannot handle, so the turn drives no work.
        HostedWorkflowRunResult second = await state.RunOrResumeAsync("s1", 42);

        // Assert: a resume that produced no events is surfaced as a warning (possible stale checkpoint /
        // mismatched input), mirroring the Python host's zero-event restore warning.
        Assert.Empty(second.Events);
        Assert.Contains(loggerFactory.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task RunOrResumeAsync_CursorMiss_ResumesFromManagerLatestCheckpointAsync()
    {
        // Arrange: a shared checkpoint manager stands in for durable storage that outlives the in-memory
        // cursor. The first holder runs one turn; a counting workflow records count:1 in the checkpoint.
        var manager = CheckpointManager.CreateInMemory();
        var first = new HostedWorkflowState(CountingWorkflow.Build(), manager);
        HostedWorkflowRunResult firstResult = await first.RunOrResumeAsync("s1", "go");
        Assert.Contains("count:1", StringOutput(firstResult));

        // Act: a NEW holder over the SAME manager (fresh cursor, e.g. after a process restart) runs the
        // session again. With durable read-through it resumes from the manager's latest checkpoint.
        var second = new HostedWorkflowState(CountingWorkflow.Build(), manager);
        HostedWorkflowRunResult resumed = await second.RunOrResumeAsync("s1", "go")
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30));

        // Assert: the count advanced to 2, proving it resumed from the prior checkpoint rather than
        // restarting from scratch (which would yield count:1 again).
        Assert.Contains("count:2", StringOutput(resumed));
        Assert.True(second.TryGetCheckpoint("s1", out _));
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

    private static string StringOutput(HostedWorkflowRunResult result) =>
        string.Concat(
            result.Events
                .OfType<WorkflowOutputEvent>()
                .Select(e => e.Data)
                .OfType<string>());

    private static Workflow CreateEchoWorkflow() =>
        AgentWorkflowBuilder.BuildSequential(workflowName: "echo", agents: [new TestEchoAgent(name: "echo")]);

    private static Workflow CreateTestWorkflow()
    {
        var mockAgent = new Mock<AIAgent>();
        mockAgent.Setup(a => a.Name).Returns("testAgent");
        return AgentWorkflowBuilder.BuildSequential(workflowName: "wf", agents: [mockAgent.Object]);
    }
}
