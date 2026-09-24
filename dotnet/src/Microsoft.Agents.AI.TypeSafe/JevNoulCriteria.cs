// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Describes what a yes and a no mean for a <see cref="JevNoulQuestion"/>.
/// </summary>
public sealed class JevNoulCriteria
{
    /// <summary>
    /// Gets or sets what a yes, a probability near 1, means.
    /// </summary>
    [JsonPropertyName("true")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Description("What a yes, a probability near 1, means.")]
    public string? True { get; set; }

    /// <summary>
    /// Gets or sets what a no, a probability near 0, means.
    /// </summary>
    [JsonPropertyName("false")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Description("What a no, a probability near 0, means.")]
    public string? False { get; set; }
}
