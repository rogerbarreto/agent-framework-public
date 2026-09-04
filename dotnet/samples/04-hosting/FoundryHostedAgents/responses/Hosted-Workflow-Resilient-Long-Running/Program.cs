// Copyright (c) Microsoft. All rights reserved.

// Sample: a long-running countdown workflow hosted as a resilient background response.
// Each completed superstep is paired with an AgentServer response checkpoint so a restarted
// process resumes without losing confirmed output. An interrupted in-flight step can run again.

using System.Globalization;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

Env.TraversePath().Load();

var agentName = System.Environment.GetEnvironmentVariable("AGENT_NAME")
    ?? "hosted-workflow-resilient-long-running";

var start = new CountdownStartExecutor();
var countdown = new CountdownExecutor();
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
    && string.Equals(System.Environment.GetEnvironmentVariable("ENABLE_E2E_SHUTDOWN_ENDPOINT"), "true", StringComparison.OrdinalIgnoreCase))
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
internal sealed class CountdownStartExecutor() : ChatProtocolExecutor("start", new ChatProtocolExecutorOptions { AutoSendTurnToken = false })
{
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        base.ConfigureProtocol(protocolBuilder).SendsMessage<int>();

    protected override ValueTask TakeTurnAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        bool? emitEvents,
        CancellationToken cancellationToken = default)
            => context.SendMessageAsync(int.Parse(messages.Single().Text, CultureInfo.InvariantCulture), cancellationToken: cancellationToken);
}

[SendsMessage(typeof(int))]
[SendsMessage(typeof(string))]
[YieldsOutput(typeof(string))]
internal sealed class CountdownExecutor() : Executor<int>("countdown")
{
    public override async ValueTask HandleAsync(
        int message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (message <= 0)
        {
            await context.SendMessageAsync(string.Empty, targetId: "complete", cancellationToken: cancellationToken);
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        await context.YieldOutputAsync(
            message.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        await context.SendMessageAsync(message - 1, targetId: this.Id, cancellationToken: cancellationToken);
    }
}

internal sealed class CountdownCompleteExecutor() : Executor<string, string>("complete")
{
    public override ValueTask<string> HandleAsync(
        string message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(message);
}
