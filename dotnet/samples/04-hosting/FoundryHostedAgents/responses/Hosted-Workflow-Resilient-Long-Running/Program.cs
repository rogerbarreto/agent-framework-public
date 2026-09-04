// Copyright (c) Microsoft. All rights reserved.

// Sample: a long-running countdown workflow hosted as a resilient background response.
// Each completed superstep is paired with an AgentServer response checkpoint so a restarted
// process resumes without losing confirmed output. An interrupted in-flight step can run again.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

Env.TraversePath().Load();

var agentName = System.Environment.GetEnvironmentVariable("AGENT_NAME")
    ?? "hosted-workflow-resilient-long-running";
var delaySeconds = int.TryParse(
    System.Environment.GetEnvironmentVariable("COUNTDOWN_DELAY_SECONDS"),
    NumberStyles.None,
    CultureInfo.InvariantCulture,
    out int configuredDelaySeconds)
    ? configuredDelaySeconds
    : 1;
if (delaySeconds < 0)
{
    throw new InvalidOperationException("COUNTDOWN_DELAY_SECONDS must be zero or greater.");
}

var start = new CountdownStartExecutor();
var countdown = new CountdownExecutor(TimeSpan.FromSeconds(delaySeconds));
var complete = new CountdownCompleteExecutor();

Workflow workflow = new WorkflowBuilder(start)
    .AddEdge(start, countdown)
    .AddEdge(countdown, countdown)
    .AddEdge(countdown, complete)
    .WithOutputFrom(start, countdown, complete)
    .Build();

AIAgent agent = workflow.AsAIAgent(
    id: agentName,
    name: agentName,
    includeExceptionDetails: true,
    includeWorkflowOutputsInResponse: true);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFoundryResponses(
    agent,
    configure: options => options.ResilientBackground = true);

var app = builder.Build();
app.MapFoundryResponses();
Task? requestedShutdown = null;
if (app.Environment.IsDevelopment())
{
    app.MapFoundryResponses("openai/v1");
}

// This configuration is for local development demonstration purposes only.
// When hosted in Foundry the lifetime of the agent process is managed and shutdowns are handled gracefully.
if (app.Environment.IsDevelopment()
    && string.Equals(
        System.Environment.GetEnvironmentVariable("ENABLE_E2E_SHUTDOWN_ENDPOINT"), "true", StringComparison.OrdinalIgnoreCase))
{
    app.MapPost(
        "/shutdown",
        (IEnumerable<IHostedService> hostedServices) =>
    {
        // The E2E closes its client stream first, then signals the resilient task service before
        // stopping HTTP. This reproduces the hosted-service shutdown path used by AgentServer tests.
        IHostedService taskDurabilityService = hostedServices.SingleOrDefault(
            service => string.Equals(
                service.GetType().FullName,
                "Azure.AI.AgentServer.Core.Tasks.Engine.TaskDurabilityService",
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "The AgentServer resilient task service is not registered.");
        requestedShutdown ??= StopServerAsync(
            app,
            taskDurabilityService);
        return Results.Accepted();
    });
}

Console.WriteLine($"Process ID: {System.Environment.ProcessId}");
await app.RunAsync();
if (requestedShutdown is not null)
{
    await requestedShutdown;
}

static async Task StopServerAsync(
    WebApplication app,
    IHostedService taskDurabilityService)
{
    await Task.Delay(TimeSpan.FromMilliseconds(100));
    await taskDurabilityService.StopAsync(CancellationToken.None);
    await app.StopAsync();
}

[SendsMessage(typeof(int))]
[YieldsOutput(typeof(string))]
internal sealed partial class CountdownStartExecutor() : ChatProtocolExecutor(
    "start",
    new ChatProtocolExecutorOptions { AutoSendTurnToken = false })
{
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        base.ConfigureProtocol(protocolBuilder).SendsMessage<int>();

    protected override async ValueTask TakeTurnAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        bool? emitEvents,
        CancellationToken cancellationToken = default)
    {
        string input = string.Join(
            System.Environment.NewLine,
            messages.Select(message => message.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
        Match match = PositiveIntegerRegex().Match(input);
        if (!match.Success
            || !int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int target)
            || target <= 0)
        {
            await context.YieldOutputAsync(
                "The message must contain a positive integer counter target.",
                cancellationToken);
            return;
        }

        await context.SendMessageAsync(target, cancellationToken: cancellationToken);
    }

    [GeneratedRegex(@"(?<!\d)\d+(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PositiveIntegerRegex();
}

[SendsMessage(typeof(int))]
[SendsMessage(typeof(string))]
[YieldsOutput(typeof(string))]
internal sealed class CountdownExecutor(TimeSpan delay) : Executor<int>("countdown")
{
    public override async ValueTask HandleAsync(
        int message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (message <= 0)
        {
            await context.SendMessageAsync(
                "Countdown complete.",
                targetId: "complete",
                cancellationToken: cancellationToken);
            return;
        }

        await Task.Delay(delay, cancellationToken);
        await context.YieldOutputAsync(
            message.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        await context.SendMessageAsync(
            message - 1,
            targetId: "countdown",
            cancellationToken: cancellationToken);
    }
}

[YieldsOutput(typeof(string))]
internal sealed class CountdownCompleteExecutor() : Executor<string>("complete")
{
    public override ValueTask HandleAsync(
        string message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default) =>
        context.YieldOutputAsync(message, cancellationToken);
}

internal sealed class IdempotentOperationService(string stateRoot)
{
    private readonly string _operationsPath =
        Path.Combine(stateRoot, "completed-operations.txt");
    private readonly HashSet<string> _completedOperations =
        File.Exists(Path.Combine(stateRoot, "completed-operations.txt"))
            ? [.. File.ReadAllLines(
                Path.Combine(stateRoot, "completed-operations.txt"))]
            : [];

    public async Task ExecuteAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stateRoot);
        if (!this._completedOperations.Add(operationId))
        {
            Console.WriteLine(
                $"Duplicate operation ignored: {operationId}");
            return;
        }

        try
        {
            await File.AppendAllTextAsync(
                this._operationsPath,
                operationId + System.Environment.NewLine,
                Encoding.UTF8,
                cancellationToken);
            Console.WriteLine($"Operation executed: {operationId}");
        }
        catch
        {
            this._completedOperations.Remove(operationId);
            throw;
        }
    }
}
