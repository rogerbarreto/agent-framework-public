// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Directs where the host delivers an agent response. Independent of <see cref="SessionMode"/>.
/// Use the static factories (<see cref="Channel"/>, <see cref="Channels"/>, <see cref="Identity"/>,
/// <see cref="Identities"/>) and the <see cref="Originating"/>, <see cref="Active"/>,
/// <see cref="AllLinked"/>, <see cref="None"/> singletons.
/// </summary>
public abstract record ResponseTarget
{
    private ResponseTarget() { }

    /// <summary>Reply on the originating channel only. The default.</summary>
    public static ResponseTarget Originating { get; } = new OriginatingResponseTarget();

    /// <summary>Reply on the channel the user was last seen on, per <see cref="IHostStateStore.GetLastSeenAsync"/>.</summary>
    public static ResponseTarget Active { get; } = new ActiveResponseTarget();

    /// <summary>Fan out to every linked identity on every channel.</summary>
    public static ResponseTarget AllLinked { get; } = new AllLinkedResponseTarget();

    /// <summary>Suppress the response entirely. The originating wire returns a <see cref="ContinuationToken"/>.</summary>
    public static ResponseTarget None { get; } = new NoneResponseTarget();

    /// <summary>Deliver to every linked identity on the named channel.</summary>
    public static ResponseTarget Channel(string channelName, bool echoInput = false)
    {
        if (channelName is null) { throw new ArgumentNullException(nameof(channelName)); }
        return new ChannelResponseTarget(channelName, echoInput);
    }

    /// <summary>Deliver to every linked identity on each of the named channels.</summary>
    public static ResponseTarget Channels(IReadOnlyList<string> channelNames, bool echoInput = false)
    {
        if (channelNames is null) { throw new ArgumentNullException(nameof(channelNames)); }
        return new ChannelsResponseTarget(channelNames, echoInput);
    }

    /// <summary>Deliver to a single specific channel-native identity.</summary>
    public static ResponseTarget Identity(ChannelIdentity identity, bool echoInput = false)
    {
        if (identity is null) { throw new ArgumentNullException(nameof(identity)); }
        return new IdentitiesResponseTarget([identity], echoInput);
    }

    /// <summary>Deliver to each of the specific channel-native identities.</summary>
    public static ResponseTarget Identities(IReadOnlyList<ChannelIdentity> identities, bool echoInput = false)
    {
        if (identities is null) { throw new ArgumentNullException(nameof(identities)); }
        return new IdentitiesResponseTarget(identities, echoInput);
    }

    /// <summary>Reply on the originating channel only.</summary>
    public sealed record OriginatingResponseTarget : ResponseTarget;

    /// <summary>Reply on the channel the user was last seen on.</summary>
    public sealed record ActiveResponseTarget : ResponseTarget;

    /// <summary>Fan out to every linked identity on every channel.</summary>
    public sealed record AllLinkedResponseTarget : ResponseTarget;

    /// <summary>Suppress the response entirely.</summary>
    public sealed record NoneResponseTarget : ResponseTarget;

    /// <summary>Deliver to a single channel.</summary>
    public sealed record ChannelResponseTarget(string ChannelName, bool EchoInput) : ResponseTarget;

    /// <summary>Deliver to multiple channels.</summary>
    public sealed record ChannelsResponseTarget(IReadOnlyList<string> ChannelNames, bool EchoInput) : ResponseTarget;

    /// <summary>Deliver to specific identities.</summary>
    public sealed record IdentitiesResponseTarget(IReadOnlyList<ChannelIdentity> Targets, bool EchoInput) : ResponseTarget;
}