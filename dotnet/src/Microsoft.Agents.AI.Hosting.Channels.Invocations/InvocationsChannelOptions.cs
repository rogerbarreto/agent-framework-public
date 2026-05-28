// Copyright (c) Microsoft. All rights reserved.

using System;

namespace Microsoft.Agents.AI.Hosting.Channels.Invocations;

/// <summary>
/// Configuration for <see cref="InvocationsChannel"/>.
/// </summary>
public sealed class InvocationsChannelOptions
{
    /// <summary>
    /// Mount root for the invocations routes. The channel exposes <c>{Path}/invoke</c> (sync run)
    /// and <c>{Path}/{continuationToken}</c> (poll). Default <c>"/invocations"</c>.
    /// </summary>
    public string Path { get; set; } = "/invocations";

    /// <summary>
    /// Optional per-channel allowlist. When <see langword="null"/> the host's
    /// <see cref="AgentFrameworkHostOptions.DefaultAllowlist"/> applies.
    /// </summary>
    public IIdentityAllowlist? Allowlist { get; set; }

    /// <summary>
    /// Optional run hook invoked after the channel produces the default request and before the host
    /// calls the runner. Apps use this to project domain-specific request shapes onto
    /// <see cref="ChannelRequest"/>.
    /// </summary>
    public IChannelRunHook? RunHook { get; set; }

    /// <summary>
    /// Optional response hook invoked per destination. The default <see cref="InvocationsChannel"/>
    /// uses this on the originating reply to project the result onto the JSON wire envelope.
    /// </summary>
    public IChannelResponseHook? ResponseHook { get; set; }
}