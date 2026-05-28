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
    /// <summary>Originating channel name (matches <see cref="Channels.Channel.Name"/>).</summary>
    public required string Channel { get; init; }

    /// <summary>Operation kind: "message.create", "command.invoke", "approval.respond", ...</summary>
    public required string Operation { get; init; }

    /// <summary>
    /// Target input. Reuses framework input types; boxed because the union spans
    /// <see cref="AIAgent"/> inputs, <see cref="ChatMessage"/> arrays, and workflow-typed inputs.
    /// </summary>
    public required object Input { get; init; }

    /// <summary>Session hint. <see langword="null"/> for ephemeral or host-tracked channels.</summary>
    public ChannelSession? Session { get; init; }

    /// <summary>Channel-native user identity. <see langword="null"/> for anonymous channels.</summary>
    public ChannelIdentity? Identity { get; init; }

    /// <summary>Protocol-visible conversation / thread / topic id, when distinct from <see cref="ChannelIdentity.NativeId"/>.</summary>
    public string? ConversationId { get; init; }

    /// <summary>
    /// Caller-derived chat options forwarded onto the runner's <see cref="ChatOptions"/>.
    /// </summary>
    public ChatOptions? Options { get; init; }

    /// <summary>How the host should resolve session continuity for this request.</summary>
    public SessionMode SessionMode { get; init; } = SessionMode.Auto;

    /// <summary>Protocol-level metadata for telemetry. The host never reads this.</summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        ImmutableDictionary<string, object?>.Empty;

    /// <summary>
    /// Channel-specific structured values surfaced to the run hook. Reserved keys for workflow
    /// targets: <c>"workflow.checkpoint_id"</c>, <c>"workflow.resume_token"</c>.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } =
        ImmutableDictionary<string, object?>.Empty;

    /// <summary>
    /// Bidirectional, mutable per-request state slot for event-rich front-ends (AG-UI).
    /// Opaque to the host.
    /// </summary>
    public IDictionary<string, object?>? ClientState { get; init; }

    /// <summary>
    /// Frontend tool catalog supplied per request. Forwarded onto <see cref="ChatOptions"/>
    /// but the host never invokes them.
    /// </summary>
    public IReadOnlyList<AITool>? ClientTools { get; init; }

    /// <summary>Pass-through bag for channel-protocol extras (AG-UI <c>resume</c>, <c>command</c>, ...).</summary>
    public IReadOnlyDictionary<string, object?>? ForwardedProps { get; init; }

    /// <summary>Whether the channel is calling <see cref="IChannelContext.StreamAsync"/> rather than <see cref="IChannelContext.RunAsync"/>.</summary>
    public bool Stream { get; init; }

    /// <summary>Where the response is delivered. <see langword="null"/> defaults to <see cref="Channels.ResponseTarget.Originating"/>.</summary>
    public ResponseTarget? ResponseTarget { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the host returns a <see cref="ContinuationToken"/> immediately
    /// rather than awaiting the response. Forced <see langword="true"/> when <see cref="ResponseTarget"/>
    /// is <see cref="Channels.ResponseTarget.None"/>.
    /// </summary>
    public bool Background { get; init; }
}