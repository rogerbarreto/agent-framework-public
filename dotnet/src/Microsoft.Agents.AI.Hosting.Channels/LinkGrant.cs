// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Pending link grant: one-time code or OAuth state issued by an <see cref="IIdentityLinker"/>
/// and persisted on the <see cref="IHostStateStore"/> until consumed by the callback / completion call.
/// </summary>
/// <param name="Code">The opaque code or state value the verifier presents.</param>
/// <param name="IssuedByLinker">The <see cref="IIdentityLinker.Name"/> that issued this grant.</param>
/// <param name="RequestedIsolationKey">Optional explicit isolation key the user requested at begin time.</param>
/// <param name="ExpiresAt">When the grant becomes invalid.</param>
/// <param name="Payload">Linker-defined opaque payload (PKCE verifier, redirect uri, ...).</param>
public sealed record LinkGrant(
    string Code,
    string IssuedByLinker,
    string? RequestedIsolationKey,
    DateTimeOffset ExpiresAt,
    IReadOnlyDictionary<string, object?> Payload);