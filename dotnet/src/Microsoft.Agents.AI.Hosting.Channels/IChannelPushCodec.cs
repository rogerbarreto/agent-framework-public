// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Nodes;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Capability interface: encode the full push envelope (context + payload) so a JSON-payload
/// <see cref="IDurableTaskRunner"/> (out-of-process worker, gRPC TaskHub, ...) can reconstruct
/// it on the receiving side. Required pairing rule is validated by the host at startup.
/// </summary>
public interface IChannelPushCodec
{
    /// <summary>Encode the push envelope.</summary>
    JsonNode Encode(ChannelPushContext context, HostedRunResult payload);

    /// <summary>Decode a previously-encoded push envelope.</summary>
    (ChannelPushContext Context, HostedRunResult Payload) Decode(JsonNode encoded);
}