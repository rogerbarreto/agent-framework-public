// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels.Responses;

/// <summary>
/// Configuration for <see cref="ResponsesChannel"/>.
/// </summary>
public sealed class ResponsesChannelOptions
{
    /// <summary>Mount root for the Responses route. Default <c>"/responses"</c>; use <c>""</c> for the app root.</summary>
    public string Path { get; set; } = "/responses";

    /// <summary>
    /// Optional run hook invoked after the channel parses the request and before the host invokes the target.
    /// </summary>
    public IChannelRunHook? RunHook { get; set; }

    /// <summary>Optional response hook invoked before the channel serializes the originating response.</summary>
    public IChannelResponseHook? ResponseHook { get; set; }

    /// <summary>
    /// Optional stream-update hook applied by the host while the channel consumes streamed updates, before
    /// the channel renders Server-Sent-Events. Applies only to streaming requests.
    /// </summary>
    public IChannelStreamTransformHook? StreamTransformHook { get; set; }
}
