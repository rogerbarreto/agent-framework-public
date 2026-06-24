// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Non-generic base for run results so channels and the host can pass them through capability
/// interfaces without committing to a result type. Inspect <see cref="ResultObject"/>
/// or cast to <see cref="HostedRunResult{TResult}"/> for typed access.
/// </summary>
public abstract record HostedRunResult
{
    /// <summary>Session attached to this result, when present.</summary>
    public ChannelSession? Session { get; init; }

    /// <summary>The wrapped result as a boxed object.</summary>
    public abstract object? ResultObject { get; }
}

/// <summary>
/// Typed run result envelope. <typeparamref name="TResult"/> is <c>AgentRunResponse</c> for
/// agent targets and <c>WorkflowRunResponse</c> for workflow targets.
/// </summary>
public sealed record HostedRunResult<TResult> : HostedRunResult
{
    /// <summary>The typed result.</summary>
    public required TResult Result { get; init; }

    /// <inheritdoc />
    public override object? ResultObject => this.Result;

    /// <summary>
    /// Shallow clone with a rewritten <see cref="Result"/>; used by per-destination
    /// <see cref="IChannelResponseHook"/> rebinds.
    /// </summary>
    public HostedRunResult<TNew> Replace<TNew>(TNew newResult) =>
        new() { Result = newResult, Session = this.Session };
}
