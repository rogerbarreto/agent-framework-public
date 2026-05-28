// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Payload the host enqueues on the durable runner under the "hosting.push" handler. Carries
/// everything the push handler needs to invoke the right channel's IChannelPush and run per-destination
/// response hooks.
/// </summary>
internal sealed record HostingPushPayload(
    ChannelPushContext PushContext,
    HostedRunResult Result,
    string DestinationChannelName,
    bool Originating);