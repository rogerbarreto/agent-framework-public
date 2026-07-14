// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
        // mismatched input).
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

    [Fact]
    public async Task RunOrResumeAsync_ConcurrentSameSessionTurns_AreSerializedAsync()
    {
        // Arrange: a workflow that signals when a turn enters and then blocks on a gate, so the test can
        // hold the first turn "inside" the workflow while it starts a second turn for the same session.
        using var entered = new SemaphoreSlim(0, 2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new HostedWorkflowState(GatedCountingWorkflow.Build(entered, release.Task), CheckpointManager.CreateInMemory());

        // Act: start the first turn and wait until it is running inside the workflow.
        Task<HostedWorkflowRunResult> first = state.RunOrResumeAsync("s1", "go").AsTask();
        Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(10)), "the first turn should enter the workflow");

        // Start a second same-session turn while the first is still inside the workflow.
        Task<HostedWorkflowRunResult> second = state.RunOrResumeAsync("s1", "go").AsTask();

        // Assert: the second turn must WAIT on the holder lock, not fault. Without the lock it would reach the
        // engine's concurrent-run ownership guard and fault (completing the task); the lock instead leaves it
        // pending until the first turn releases. Checking the task is not completed isolates the holder lock
        // from the engine guard, and the entered gate confirms it did not run concurrently.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(second.IsCompleted, "the second turn must wait on the holder lock rather than fault or run concurrently");
        Assert.False(await entered.WaitAsync(TimeSpan.FromMilliseconds(200)), "the second turn must not enter the workflow while the first holds it");

        // Release both turns and let them run to completion.
        release.SetResult();
        HostedWorkflowRunResult[] results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));

        // Both turns completed successfully (proving the lock serialized rather than faulted them), advancing
        // the count from 1 to 2.
        string combined = string.Concat(results.Select(StringOutput));
        Assert.Contains("count:1", combined);
        Assert.Contains("count:2", combined);
    }

    [Fact]
    public async Task RunOrResumeAsync_NonChatWorkflow_ResumesWithNewInputAsync()
    {
        // Arrange: a non-chat-protocol workflow (string start executor), so the resume path sends the input
        // without a TurnToken.
        var state = new HostedWorkflowState(CountingWorkflow.Build());
        HostedWorkflowRunResult first = await state.RunOrResumeAsync("s1", "go");
        Assert.Contains("count:1", StringOutput(first));

        // Act
        HostedWorkflowRunResult second = await state.RunOrResumeAsync("s1", "go")
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30));

        // Assert: the non-chat resume carried state and advanced the checkpoint.
        Assert.Contains("count:2", StringOutput(second));
        Assert.NotNull(second.Checkpoint);
        Assert.NotSame(first.Checkpoint, second.Checkpoint);
    }

    [Fact]
    public async Task RunOrResumeAsync_ThirdTurn_KeepsAdvancingCheckpointAsync()
    {
        // Arrange
        var state = new HostedWorkflowState(CreateEchoWorkflow());

        // Act: three turns on the same session.
        HostedWorkflowRunResult r1 = await state.RunOrResumeAsync("s1", InputMessages("a"));
        HostedWorkflowRunResult r2 = await state.RunOrResumeAsync("s1", InputMessages("b"))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30));
        HostedWorkflowRunResult r3 = await state.RunOrResumeAsync("s1", InputMessages("c"))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30));

        // Assert: the cursor keeps advancing past the second turn, and the head reflects the latest turn.
        Assert.Contains("c", OutputText(r3));
        Assert.NotNull(r1.Checkpoint);
        Assert.NotNull(r3.Checkpoint);
        Assert.NotSame(r1.Checkpoint, r2.Checkpoint);
        Assert.NotSame(r2.Checkpoint, r3.Checkpoint);
        Assert.True(state.TryGetCheckpoint("s1", out CheckpointInfo? head));
        Assert.Same(r3.Checkpoint, head);
    }

    [Fact]
    public async Task RunOrResumeStreamingAsync_StreamsEventsAndResumesAsync()
    {
        // Arrange
        var state = new HostedWorkflowState(CreateEchoWorkflow());

        // Act: first turn streamed.
        List<WorkflowEvent> firstEvents = [];
        await foreach (WorkflowEvent evt in state.RunOrResumeStreamingAsync("s1", InputMessages("hello")))
        {
            firstEvents.Add(evt);
        }

        // Assert: events streamed and the checkpoint was recorded after the stream completed.
        Assert.NotEmpty(firstEvents);
        Assert.True(state.TryGetCheckpoint("s1", out CheckpointInfo? firstCheckpoint));
        Assert.NotNull(firstCheckpoint);

        // Act: second turn streamed via the resume path with new input.
        List<WorkflowEvent> secondEvents = [];
        await foreach (WorkflowEvent evt in state.RunOrResumeStreamingAsync("s1", InputMessages("world")))
        {
            secondEvents.Add(evt);
        }

        // Assert: the resumed stream processed the new input and advanced the checkpoint.
        string output = string.Concat(
            secondEvents
                .OfType<WorkflowOutputEvent>()
                .Select(e => e.Data)
                .OfType<IEnumerable<ChatMessage>>()
                .SelectMany(messages => messages)
                .Select(m => m.Text));
        Assert.Contains("world", output);
        Assert.True(state.TryGetCheckpoint("s1", out CheckpointInfo? secondCheckpoint));
        Assert.NotSame(firstCheckpoint, secondCheckpoint);
    }

    [Fact]
    public async Task RunOrResumeAsync_AdaptsResponsesInputToTypedStartExecutorAsync()
    {
        // Arrange: a workflow whose start executor takes a typed WriterBrief rather than List<ChatMessage>.
        // The application adapts the Responses input into that type before calling RunOrResumeAsync via the
        // generic TInput.
        var state = new HostedWorkflowState(BriefWorkflow.Build());

        // Simulate parsing a structured Responses text payload into the start executor's input type.
        const string ResponsesText = "{\"topic\":\"electric SUV\",\"style\":\"playful\"}";
        using JsonDocument doc = JsonDocument.Parse(ResponsesText);
        var brief = new BriefWorkflow.WriterBrief(
            doc.RootElement.GetProperty("topic").GetString()!,
            doc.RootElement.GetProperty("style").GetString()!);

        // Act
        HostedWorkflowRunResult result = await state.RunOrResumeAsync("s1", brief);

        // Assert: the adapted input drove the typed start executor.
        Assert.Contains("[playful] electric SUV", StringOutput(result));
    }

    [Fact]
    public async Task RunOrResumeAsync_ResumeWithRejectedInput_DoesNotHangAsync()
    {
        // Arrange: a non-chat human-in-the-loop workflow whose first turn emits a request and halts.
        var state = new HostedWorkflowState(ApprovalGateWorkflow.Build());
        HostedWorkflowRunResult first = await state.RunOrResumeAsync("s1", "approve")
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Contains(first.Events, e => e is RequestInfoEvent);

        // Act: resume with an input the start executor cannot handle (wrong type), so no superstep runs.
        // A drain that blocks on the restored pending request would hang here; guard with a timeout.
        HostedWorkflowRunResult second = await state.RunOrResumeAsync("s1", 42)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30));

        // Assert: it returned (surfacing the restored pending request) rather than blocking indefinitely.
        Assert.Contains(second.Events, e => e is RequestInfoEvent);
    }

    [Fact]
    public async Task RunOrResumeAsync_ResumeSuperstepWithRequestAndDownstream_DoesNotTruncateAsync()
    {
        // Arrange: a workflow whose start executor, in one superstep, emits a request AND queues a message to
        // a downstream executor that yields output. The first turn establishes a checkpoint.
        var state = new HostedWorkflowState(FanOutRequestWorkflow.Build());
        HostedWorkflowRunResult first = await state.RunOrResumeAsync("s1", "one")
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Contains(first.Events, e => e is RequestInfoEvent);

        // Act: resume with new input, which again fans out to the request port and the downstream executor.
        HostedWorkflowRunResult second = await state.RunOrResumeAsync("s1", "two")
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30));

        // Assert: the resumed turn drained past the request-bearing superstep so the downstream output is
        // present (a drain that broke at the request would truncate it).
        Assert.Contains(second.Events, e => e is RequestInfoEvent);
        Assert.Contains(FanOutRequestWorkflow.DownstreamPrefix, StringOutput(second));
    }

    [Fact]
    public async Task RunOrResumeStreamingAsync_AbandonedAfterCheckpoint_AdvancesCursorAsync()
    {
        // Arrange
        var state = new HostedWorkflowState(CreateEchoWorkflow());
        await foreach (WorkflowEvent _ in state.RunOrResumeStreamingAsync("s1", InputMessages("a")))
        {
            // Enumerate the first turn to completion so the cursor holds its head checkpoint.
        }
        Assert.True(state.TryGetCheckpoint("s1", out CheckpointInfo? cp1));

        // Act: abandon the second turn after a superstep has committed a checkpoint.
        await foreach (WorkflowEvent evt in state.RunOrResumeStreamingAsync("s1", InputMessages("b")))
        {
            if (evt is SuperStepCompletedEvent { CompletionInfo.Checkpoint: not null })
            {
                break;
            }
        }

        // Assert: the abandoned turn still advanced the cursor to the last committed checkpoint, so a later
        // turn resumes from there rather than re-running from the previous head.
        Assert.True(state.TryGetCheckpoint("s1", out CheckpointInfo? cp2));
        Assert.NotEqual(cp1, cp2);
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
