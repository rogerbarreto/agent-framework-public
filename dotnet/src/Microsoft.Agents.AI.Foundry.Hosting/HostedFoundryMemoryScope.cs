// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Selects which <see cref="HostedFoundryMemoryProviderScopes"/> helper a DI registration uses to
/// scope memories.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
public enum HostedFoundryMemoryScope
{
    /// <summary>Per end user. Maps to <see cref="HostedFoundryMemoryProviderScopes.PerUser"/>.</summary>
    PerUser,

    /// <summary>Per conversation. Maps to <see cref="HostedFoundryMemoryProviderScopes.PerChat"/>.</summary>
    PerChat,

    /// <summary>Per (user, chat) pair. Maps to <see cref="HostedFoundryMemoryProviderScopes.PerUserAndChat"/>.</summary>
    PerUserAndChat
}
