// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// The body of a TypeSafe System One API request: a <see cref="JevRequest"/> plus the model that answers it.
/// </summary>
internal sealed class JevApiRequest
{
    [JsonPropertyName("state")]
    public required JsonElement State { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("questions")]
    public required IReadOnlyDictionary<string, JevQuestion> Questions { get; init; }
}
