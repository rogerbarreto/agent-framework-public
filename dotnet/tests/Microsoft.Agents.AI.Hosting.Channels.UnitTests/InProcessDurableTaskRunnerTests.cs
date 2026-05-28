// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.Channels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Agents.AI.Hosting.Channels.UnitTests;

public class InProcessDurableTaskRunnerTests
{
    [Fact]
    public async Task Schedule_InvokesHandler_AndReachesSucceeded()
    {
        // Arrange
        var runner = new InProcessDurableTaskRunner(NullLogger<InProcessDurableTaskRunner>.Instance);
        await runner.StartAsync(CancellationToken.None);

        var ran = new TaskCompletionSource<int>();
        runner.Register("test", _ => { ran.SetResult(42); return ValueTask.CompletedTask; });

        // Act
        var handle = await runner.ScheduleAsync("test", payload: new object(), retryPolicy: null, CancellationToken.None);
        var observed = await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Allow the runner to record the final status.
        DurableTaskStatus? status = null;
        for (var i = 0; i < 20 && (status = await runner.GetAsync(handle, CancellationToken.None)) != DurableTaskStatus.Succeeded; i++)
        {
            await Task.Delay(50);
        }

        // Assert
        Assert.Equal(42, observed);
        Assert.Equal(DurableTaskStatus.Succeeded, status);

        await runner.StopAsync(CancellationToken.None);
        await runner.DisposeAsync();
    }

    [Fact]
    public async Task Schedule_RetriesOnException_BeforeGivingUp()
    {
        // Arrange
        var runner = new InProcessDurableTaskRunner(NullLogger<InProcessDurableTaskRunner>.Instance);
        await runner.StartAsync(CancellationToken.None);

        var attempts = 0;
        runner.Register("flaky", _ => { attempts++; throw new InvalidOperationException("boom"); });

        // Act
        var handle = await runner.ScheduleAsync("flaky", payload: new object(), retryPolicy: new RetryPolicy { MaxAttempts = 3, InitialBackoff = TimeSpan.FromMilliseconds(1), MaxBackoff = TimeSpan.FromMilliseconds(5) }, CancellationToken.None);

        // Allow retries to play out.
        DurableTaskStatus? status = null;
        for (var i = 0; i < 50 && (status = await runner.GetAsync(handle, CancellationToken.None)) != DurableTaskStatus.Failed; i++)
        {
            await Task.Delay(50);
        }

        // Assert
        Assert.Equal(DurableTaskStatus.Failed, status);
        Assert.Equal(3, attempts);

        await runner.StopAsync(CancellationToken.None);
        await runner.DisposeAsync();
    }
}