// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Channel-native user identity observed on a <see cref="ChannelRequest"/>.
/// </summary>
/// <param name="Channel">The originating channel name (matches <see cref="Channel.Name"/>).</param>
/// <param name="NativeId">The channel-native USER identifier (never the chat or conversation id).</param>
public sealed record ChannelIdentity(string Channel, string NativeId)
{
    /// <summary>
    /// Channel-defined attributes attached to this identity (e.g. display name, language).
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
