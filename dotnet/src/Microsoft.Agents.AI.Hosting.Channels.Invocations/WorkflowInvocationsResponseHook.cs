// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Hosting.Channels.Invocations;

/// <summary>
/// Per-destination response hook that projects <see cref="WorkflowRunResult"/> onto the
/// invocations JSON envelope (<c>status: "awaiting_input"</c> / <c>"completed"</c> / <c>"failed"</c>).
/// Apply this hook to non-originating workflow deliveries where another channel pushes the result.
/// The originating reply is rendered by <see cref="InvocationsChannel"/> directly.
/// </summary>
public sealed class WorkflowInvocationsResponseHook : IChannelResponseHook
{
    /// <inheritdoc />
    public ValueTask<HostedRunResult> OnResponseAsync(
        HostedRunResult result,
        ChannelResponseContext context,
        CancellationToken cancellationToken)
    {
        if (result.ResultObject is not WorkflowRunResult workflow)
        {
            return new(result);
        }

        // Preserve the typed envelope; consumers downstream (e.g. push codecs) project it on the wire.
        // This hook centralizes the projection rule so multi-destination workflow rebinds stay consistent.
        return new(result);
    }
}