// Copyright (c) Microsoft. All rights reserved.

// Sample: a long-running countdown workflow hosted as a resilient background response.
// Each completed superstep is paired with an AgentServer response checkpoint so a restarted
// process resumes without losing confirmed output. An interrupted in-flight step can run again.

using System.Globalization;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.Sqlite;
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
    private readonly SqliteIdempotencyService _idempotencyService = new();

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
        string result = await this._idempotencyService.ExecuteAsync(
            message,
            cancellationToken);
        await context.YieldOutputAsync(
            result,
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

/// <summary>
/// For demonstration purposes only, a simple idempotency service that uses a local SQLite database to store completed countdown values.
/// </summary>
/// <remarks>
/// When dealing with a resilient long-running background process, depending on the failure
/// the recovery may replay non-saved checkpoint before a crash.
/// Ensuring that any API's called from this process are idempotent and able to handle gracefully
/// multiple similar calls can prevent unintended side effects downstream.
/// </remarks>
internal sealed class SqliteIdempotencyService
{
    private readonly string _connectionString;

    public SqliteIdempotencyService()
    {
        string stateRoot =
            System.Environment.GetEnvironmentVariable("AGENTSERVER_STATE_ROOT")
            ?? System.Environment.GetEnvironmentVariable("HOME")
            ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(stateRoot);
        this._connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(stateRoot, "countdown-operations.db"),
        }.ToString();

        using var connection = new SqliteConnection(this._connectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS countdown_operations (
                count_value INTEGER PRIMARY KEY,
                result TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public async Task<string> ExecuteAsync(
        int count,
        CancellationToken cancellationToken)
    {
        string result = count.ToString(CultureInfo.InvariantCulture);
        await using var connection =
            new SqliteConnection(this._connectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT OR IGNORE INTO countdown_operations (count_value, result)
            VALUES ($count, $result);
            """;
        insert.Parameters.AddWithValue("$count", count);
        insert.Parameters.AddWithValue("$result", result);
        if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            Console.WriteLine(
                $"Operation {count} executed and stored in SQLite.");
            return result;
        }

        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText =
            """
            SELECT result
            FROM countdown_operations
            WHERE count_value = $count;
            """;
        select.Parameters.AddWithValue("$count", count);
        string storedResult =
            (string?)await select.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"Operation {count} exists without a stored result.");
        Console.WriteLine(
            $"Operation {count} already exists in SQLite. Returning stored result.");
        return storedResult;
    }
}
