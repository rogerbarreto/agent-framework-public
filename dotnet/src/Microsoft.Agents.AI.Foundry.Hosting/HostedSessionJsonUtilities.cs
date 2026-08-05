// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// JSON serialization utilities for hosted session identity types.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
internal static class HostedSessionJsonUtilities
{
    /// <summary>
    /// Default JSON serializer options for hosted session state.
    /// </summary>
    public static JsonSerializerOptions DefaultOptions { get; } = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        TypeInfoResolver = HostedSessionJsonContext.Default
    };
}

/// <summary>
/// The persisted shape of a <see cref="ChatClientAgentSession"/> whose conversation lives in
/// <see cref="AgentSession.StateBag"/> alone, with no <c>conversationId</c> because no conversation on
/// the service corresponds to it.
/// </summary>
internal sealed class InMemorySessionStateConversation
{
    /// <summary>Gets or sets the state the session carries.</summary>
    [JsonPropertyName("stateBag")]
    public AgentSessionStateBag? StateBag { get; set; }
}

/// <summary>
/// Source-generated JSON serialization context for hosted session identity types.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.General,
    UseStringEnumConverter = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(HostedSessionContext))]
[JsonSerializable(typeof(InMemorySessionStateConversation))]
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
internal partial class HostedSessionJsonContext : JsonSerializerContext;
