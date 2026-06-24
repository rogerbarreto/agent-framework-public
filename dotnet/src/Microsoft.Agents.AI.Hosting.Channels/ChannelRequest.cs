// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Channel-neutral request envelope handed to <see cref="IChannelContext.RunAsync"/> /
/// <see cref="IChannelContext.StreamAsync"/>. Channels build this from their wire format.
/// </summary>
public sealed record ChannelRequest
{
    /// <summary>Originating channel name (matches <see cref="Channel.Name"/>).</summary>
    public required string Channel { get; init; }

    /// <summary>Operation kind: "message.create", "command.invoke", ...</summary>
    public required string Operation { get; init; }

    /// <summary>Target input: string, <see cref="ChatMessage"/>, <see cref="ChatMessage"/> sequence, or workflow input.</summary>
    public required object Input { get; init; }

    /// <summary>Session hint. <see langword="null"/> for ephemeral requests.</summary>
    public ChannelSession? Session { get; init; }

    /// <summary>Channel-native user identity. Request metadata only; not a linking, authorization, or delivery key.</summary>
    public ChannelIdentity? Identity { get; init; }

    /// <summary>Protocol-visible conversation / thread id, when distinct from <see cref="ChannelIdentity.NativeId"/>.</summary>
    public string? ConversationId { get; init; }

    /// <summary>Caller-derived chat options forwarded onto the runner's <see cref="ChatOptions"/>.</summary>
    public ChatOptions? Options { get; init; }

    /// <summary>How the host resolves session continuity for this request.</summary>
    public SessionMode SessionMode { get; init; } = SessionMode.Auto;

    /// <summary>Protocol-level metadata for telemetry. The host never reads this.</summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = ImmutableDictionary<string, object?>.Empty;

    /// <summary>
    /// Channel-specific structured values surfaced to the run hook. Reserved key for workflow targets:
    /// <c>"workflow.checkpoint_id"</c> (caller-supplied checkpoint resume).
    /// </summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = ImmutableDictionary<string, object?>.Empty;

    /// <summary>Whether the channel is calling <see cref="IChannelContext.StreamAsync"/> rather than <see cref="IChannelContext.RunAsync"/>.</summary>
    public bool Stream { get; init; }
}
