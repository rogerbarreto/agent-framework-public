// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;
using ThreadingChannel = System.Threading.Channels.Channel;
using ThreadingChannelT = System.Threading.Channels;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Default <see cref="IDurableTaskRunner"/> implementation: an <see cref="IHostedService"/> backed
/// by a bounded <see cref="System.Threading.Channels.Channel{T}"/> worker loop. In-memory only (no
/// replay across restarts); applications needing durability should swap in an external runner from
/// a fast-follow package.
/// </summary>
/// <remarks>
/// Two-phase shutdown: on <see cref="StopAsync"/>, in-flight tasks are given <see cref="ShutdownGraceSeconds"/>
/// to finish. Remaining tasks are cancelled and their cancellation exceptions are swallowed.
/// </remarks>
public sealed class InProcessDurableTaskRunner : IDurableTaskRunner, IHostedService, IAsyncDisposable
{
    private readonly ThreadingChannelT.Channel<QueuedTask> _queue;
    private readonly ConcurrentDictionary<string, Func<TaskInvocationContext, ValueTask>> _handlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DurableTaskStatus> _statuses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IDictionary<string, object?>> _state = new(StringComparer.Ordinal);
    private readonly ILogger<InProcessDurableTaskRunner> _logger;
    private readonly CancellationTokenSource _shutdownCts = new();
    private Task? _workerTask;

    /// <summary>Time the runner waits for in-flight tasks at shutdown before cancelling them.</summary>
    public double ShutdownGraceSeconds { get; init; } = 5.0;

    /// <inheritdoc />
    public DurableTaskPayloadMode PayloadMode => DurableTaskPayloadMode.Object;

    /// <summary>Initializes a new instance.</summary>
    public InProcessDurableTaskRunner(ILogger<InProcessDurableTaskRunner> logger)
    {
        this._logger = Throw.IfNull(logger);
        this._queue = ThreadingChannel.CreateBounded<QueuedTask>(new ThreadingChannelT.BoundedChannelOptions(1024)
        {
            SingleReader = true,
            FullMode = ThreadingChannelT.BoundedChannelFullMode.Wait,
        });
    }

    /// <inheritdoc />
    public void Register(string name, Func<TaskInvocationContext, ValueTask> handler)
    {
        Throw.IfNullOrEmpty(name);
        Throw.IfNull(handler);
        this._handlers[name] = handler;
    }

    /// <inheritdoc />
    public async ValueTask<TaskHandle> ScheduleAsync(string name, object payload, RetryPolicy? retryPolicy, CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(name);
        Throw.IfNull(payload);

        if (!this._handlers.ContainsKey(name))
        {
            throw new InvalidOperationException($"No handler registered under '{name}'.");
        }

        var handle = new TaskHandle(Guid.NewGuid().ToString("N"), name);
        this._statuses[handle.TaskId] = DurableTaskStatus.Scheduled;
        this._state[handle.TaskId] = new Dictionary<string, object?>(StringComparer.Ordinal);

        await this._queue.Writer.WriteAsync(new QueuedTask(handle, payload, retryPolicy ?? RetryPolicy.Default), cancellationToken).ConfigureAwait(false);
        return handle;
    }

    /// <inheritdoc />
    public ValueTask<DurableTaskStatus?> GetAsync(TaskHandle handle, CancellationToken cancellationToken)
    {
        Throw.IfNull(handle);
        return new(this._statuses.TryGetValue(handle.TaskId, out var status) ? status : null);
    }

    /// <inheritdoc />
    public ValueTask CancelAsync(TaskHandle handle, CancellationToken cancellationToken)
    {
        Throw.IfNull(handle);
        this._statuses.TryUpdate(handle.TaskId, DurableTaskStatus.Cancelled, DurableTaskStatus.Scheduled);
        return default;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        this._workerTask = Task.Run(() => this.WorkerLoopAsync(this._shutdownCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        this._queue.Writer.TryComplete();
        var grace = TimeSpan.FromSeconds(this.ShutdownGraceSeconds);
        try
        {
            if (this._workerTask is not null)
            {
                await this._workerTask.WaitAsync(grace, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            this._shutdownCts.Cancel();
            if (this._workerTask is not null)
            {
                try { await this._workerTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* expected at shutdown */ }
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        this._shutdownCts.Cancel();
        if (this._workerTask is not null)
        {
            try { await this._workerTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }
        this._shutdownCts.Dispose();
    }

    private async Task WorkerLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (var queued in this._queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (this._statuses.TryGetValue(queued.Handle.TaskId, out var status) && status == DurableTaskStatus.Cancelled)
            {
                continue;
            }

            await this.ExecuteWithRetryAsync(queued, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteWithRetryAsync(QueuedTask queued, CancellationToken cancellationToken)
    {
        if (!this._handlers.TryGetValue(queued.Handle.Name, out var handler))
        {
            this._statuses[queued.Handle.TaskId] = DurableTaskStatus.Failed;
            this._logger.LogError("No handler registered for {Handler} (task {TaskId}).", queued.Handle.Name, queued.Handle.TaskId);
            return;
        }

        var policy = queued.RetryPolicy;
        var delay = policy.InitialBackoff;
        var state = this._state[queued.Handle.TaskId];

        for (var attempt = 1; attempt <= policy.MaxAttempts; attempt++)
        {
            this._statuses[queued.Handle.TaskId] = DurableTaskStatus.Running;
            try
            {
                await handler(new TaskInvocationContext(queued.Handle.Name, queued.Payload, attempt, state)).ConfigureAwait(false);
                this._statuses[queued.Handle.TaskId] = DurableTaskStatus.Succeeded;
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                this._statuses[queued.Handle.TaskId] = DurableTaskStatus.Cancelled;
                return;
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Durable task {TaskId} attempt {Attempt}/{Max} failed.", queued.Handle.TaskId, attempt, policy.MaxAttempts);
                if (attempt == policy.MaxAttempts)
                {
                    this._statuses[queued.Handle.TaskId] = DurableTaskStatus.Failed;
                    return;
                }
                try { await Task.Delay(delay, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { this._statuses[queued.Handle.TaskId] = DurableTaskStatus.Cancelled; return; }
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * policy.BackoffMultiplier, policy.MaxBackoff.TotalMilliseconds));
            }
        }
    }

    private sealed record QueuedTask(TaskHandle Handle, object Payload, RetryPolicy RetryPolicy);
}