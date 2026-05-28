// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Operation being authorized by an <see cref="ILinkPolicy"/>.
/// </summary>
public enum LinkPolicyOperation
{
    /// <summary>Whether two channels may share an isolation key (asked by <see cref="IIdentityLinker"/>).</summary>
    Link,

    /// <summary>Whether one channel may deliver a response targeting an identity belonging to another channel.</summary>
    Deliver,
}