// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Serializer options and JSON schemas shared by the Jev tool.
/// </summary>
internal static class JevJsonUtilities
{
    private static readonly Lazy<JsonElement> s_requestSchema = new(static () => AIJsonUtilities.CreateJsonSchema(typeof(JevRequest), serializerOptions: Options));
    private static readonly Lazy<JsonElement> s_responseSchema = new(static () => AIJsonUtilities.CreateJsonSchema(typeof(JevResponse), serializerOptions: Options));

    /// <summary>
    /// Gets the options that resolve the Jev contract types first and every other type through
    /// <see cref="AIJsonUtilities.DefaultOptions"/>.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Gets the JSON schema of the tool arguments, which is the schema of <see cref="JevRequest"/>.</summary>
    public static JsonElement RequestSchema => s_requestSchema.Value;

    /// <summary>Gets the JSON schema of the tool result, which is the schema of <see cref="JevResponse"/>.</summary>
    public static JsonElement ResponseSchema => s_responseSchema.Value;

    /// <summary>
    /// Converts one tool argument into a JSON node. Chat clients pass arguments as <see cref="JsonElement"/> values;
    /// callers that invoke the function directly may pass .NET objects instead.
    /// </summary>
    public static JsonNode? ToNode(object? value) =>
        value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            JsonElement element => element.ValueKind switch
            {
                JsonValueKind.Object => JsonObject.Create(element),
                JsonValueKind.Array => JsonArray.Create(element),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => JsonValue.Create(element),
            },
            _ => JsonSerializer.SerializeToNode(value, Options.GetTypeInfo(value.GetType())),
        };

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JevJsonContext.Default.Options)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(JevJsonContext.Default, AIJsonUtilities.DefaultOptions.TypeInfoResolver),
        };

        options.MakeReadOnly();
        return options;
    }
}
