// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using static ResilienceE2EHostedServerManager;

VerificationOptions options = VerificationOptions.Parse(args);
using var cancellationSource =
    new CancellationTokenSource(TimeSpan.FromMinutes(6));

try
{
    await RunScenarioAsync(
        options,
        InterruptionKind.Crash,
        cancellationSource.Token);
    await RunScenarioAsync(
        options,
        InterruptionKind.Shutdown,
        cancellationSource.Token);

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine(
        "PASS: both recovery paths completed and the idempotent service ignored the repeated operation.");
    Console.ResetColor();
}
catch (Exception exception)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"FAIL: {exception.Message}");
    Console.ResetColor();
    System.Environment.ExitCode = 1;
}

static async Task RunScenarioAsync(
    VerificationOptions options,
    InterruptionKind interruption,
    CancellationToken cancellationToken)
{
    await using var serverManager = new ResilienceE2EHostedServerManager(options, interruption, pauseMilliseconds: 2_000);

    PrintHeader(interruption, serverManager);

    Console.WriteLine($"[{interruption} 1/6] Building the server...");
    await serverManager.BuildServerAsync(cancellationToken);

    Console.WriteLine($"[{interruption} 2/6] Starting the server...");
    Console.WriteLine($"      Process ID: {await serverManager.StartServerAsync(cancellationToken)}");

    AIAgent agent = serverManager.GetAIAgent();
    AgentSession session = await agent.CreateSessionAsync(cancellationToken);

    Console.WriteLine($"[{interruption} 3/6] Starting the background response...");
    var responseOptions = new AgentRunOptions
    {
        AllowBackgroundResponses = true,
    };
    using var connectionCancellation =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    IAsyncEnumerator<AgentResponseUpdate> response =
        agent.RunStreamingAsync(
            $"Count down from {serverManager.Options.Target}",
            session,
            responseOptions,
            connectionCancellation.Token).GetAsyncEnumerator(
                connectionCancellation.Token);
    ResponseContinuationToken responseToken;
    try
    {
        if (!await response.MoveNextAsync())
        {
            throw new InvalidOperationException(
                "The background response ended before it was accepted.");
        }

        Console.WriteLine($"      Response ID: {response.Current.ResponseId}");
        responseToken = response.Current.ContinuationToken
            ?? throw new InvalidOperationException("The accepted response did not provide a continuation token.");
    }
    finally
    {
        connectionCancellation.Cancel();
        try
        {
            await response.DisposeAsync();
        }
        catch (OperationCanceledException)
            when (connectionCancellation.IsCancellationRequested)
        {
        }
    }

    Console.WriteLine(
        $"[{interruption} 4/6] Waiting for operation {serverManager.InterruptValue}...");
    var followOptions = new AgentRunOptions
    {
        AllowBackgroundResponses = true,
        ContinuationToken = responseToken,
    };

    try
    {
        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(
            session,
            followOptions,
            cancellationToken))
        {
            IdempotentService.ExecuteOperation(update.Text);

            if (update.Text.Contains(serverManager.InterruptValue.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (interruption == InterruptionKind.Crash)
                {
                    await serverManager.CrashServerAsync();
                    serverManager.DeleteStaleStreamLocks();
                }
                else
                {
                    await serverManager.RequestShutdownAsync(cancellationToken);
                    await serverManager.WaitForServerExitAsync(cancellationToken);
                }
            }
        }
    }
    catch
    {
        Console.WriteLine("      The connection was interrupted.");
    }

    Console.WriteLine($"[{interruption} 5/6] Starting the replacement server...");
    Console.WriteLine($"      Process ID: {await serverManager.StartServerAsync(cancellationToken)}");

    Console.WriteLine($"[{interruption} 6/6] Reading the recovered response...");
    var recoveryOptions = new AgentRunOptions
    {
        AllowBackgroundResponses = true,
        ContinuationToken = responseToken,
    };
    await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(
        session,
        recoveryOptions,
        cancellationToken))
    {
        IdempotentService.ExecuteOperation(update.Text);
    }

    serverManager.MarkSucceeded();
}

static void PrintHeader(
    InterruptionKind interruption,
    ResilienceE2EHostedServerManager harness)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("============================================================");
    Console.WriteLine(interruption == InterruptionKind.Crash
        ? "Abrupt process crash"
        : "Host shutdown");
    Console.WriteLine("============================================================");
    Console.ResetColor();
    Console.WriteLine();
}
