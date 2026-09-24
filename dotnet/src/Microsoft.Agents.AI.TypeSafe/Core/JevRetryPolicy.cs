// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// The default retry policy of <see cref="JevAIToolOptions"/>: the standard <see cref="ClientRetryPolicy"/>, except
/// that it never waits longer than <see cref="MaxRetryAfter"/>.
/// </summary>
/// <remarks>
/// <see cref="ClientRetryPolicy"/> waits for whatever <c>Retry-After</c> the server sends. Inside a tool call that
/// wait blocks the agent run with no feedback, and a very large value makes the delay itself throw, so a longer
/// request fails at once with the service's status instead.
/// </remarks>
internal sealed class JevRetryPolicy : ClientRetryPolicy
{
    public const int DefaultMaxRetries = 3;

    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(1);

    public JevRetryPolicy(int maxRetries = DefaultMaxRetries, bool enableLogging = true, ILoggerFactory? loggerFactory = null)
        : base(maxRetries, enableLogging, loggerFactory)
    {
    }

    /// <summary>
    /// Gets or sets a list that, when set, receives each delay instead of waiting it, so tests can check the delays
    /// without being slowed down by them.
    /// </summary>
    internal List<TimeSpan>? RecordedWaitsForTests { get; set; }

    protected override bool ShouldRetry(PipelineMessage message, Exception? exception) =>
        base.ShouldRetry(message, exception) && IsRetryAfterWithinLimit(message);

    protected override async ValueTask<bool> ShouldRetryAsync(PipelineMessage message, Exception? exception) =>
        await base.ShouldRetryAsync(message, exception).ConfigureAwait(false) && IsRetryAfterWithinLimit(message);

    protected override Task WaitAsync(TimeSpan time, CancellationToken cancellationToken)
    {
        if (this.RecordedWaitsForTests is { } waits)
        {
            waits.Add(time);
            return Task.CompletedTask;
        }

        return base.WaitAsync(time, cancellationToken);
    }

    protected override void Wait(TimeSpan time, CancellationToken cancellationToken)
    {
        if (this.RecordedWaitsForTests is { } waits)
        {
            waits.Add(time);
            return;
        }

        base.Wait(time, cancellationToken);
    }

    private static bool IsRetryAfterWithinLimit(PipelineMessage message)
    {
        if (message.Response?.Headers.TryGetValue("Retry-After", out string? value) is not true || string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        // Retry-After is either a number of seconds or an HTTP date.
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
        {
            return seconds <= MaxRetryAfter.TotalSeconds;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date))
        {
            return date - DateTimeOffset.UtcNow <= MaxRetryAfter;
        }

        return true;
    }
}
