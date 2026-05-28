// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Session hint carried on a <see cref="ChannelRequest"/>. All fields nullable to support
/// caller-supplied (Responses, Invocations) and host-tracked (Telegram, Activity Protocol) channels.
/// </summary>
public sealed record ChannelSession
{
    /// <summary>
    /// Stable host lookup key for an <see cref="AgentSession"/>. Caller-supplied channels populate
    /// from the wire (<c>previous_response_id</c>, <c>conversation_id</c>); host-tracked channels
    /// leave this <see langword="null"/> and let the per-isolation-key alias decide.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>The protocol-visible conversation or thread identifier when one exists.</summary>
    public string? ConversationId { get; init; }

    /// <summary>Opaque isolation boundary (user, tenant, chat, ...) using hosted-agent terminology.</summary>
    public string? IsolationKey { get; init; }

    /// <summary>Channel-defined attributes; not interpreted by the host.</summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } =
        ImmutableDictionary<string, object?>.Empty;
}