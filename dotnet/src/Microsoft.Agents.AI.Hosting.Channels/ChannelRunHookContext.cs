// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Per-call context passed to <see cref="IChannelRunHook.OnRequestAsync"/>.
/// </summary>
public sealed record ChannelRunHookContext
{
    /// <summary>The runner target: an <see cref="AIAgent"/>, a workflow, or a hosted-agent handle.</summary>
    public required object Target { get; init; }

    /// <summary>The raw inbound payload as it arrived on the wire. Loosely typed.</summary>
    public object? ProtocolRequest { get; init; }
}
