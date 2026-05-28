// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Declares how an <see cref="IDurableTaskRunner"/> serializes task payloads.
/// </summary>
/// <remarks>
/// The host validates pairings at startup: if the runner is <see cref="Json"/>, every push-capable
/// channel must also implement <see cref="IChannelPushCodec"/>.
/// </remarks>
#pragma warning disable CA1720 // Identifier contains type name — Python parity (OBJECT / JSON enum values).
public enum DurableTaskPayloadMode
{
    /// <summary>Payloads are passed through as opaque .NET objects (in-process runners).</summary>
    Object,

    /// <summary>Payloads are JSON-serialized; channels must supply an <see cref="IChannelPushCodec"/>.</summary>
    Json,
}
#pragma warning restore CA1720