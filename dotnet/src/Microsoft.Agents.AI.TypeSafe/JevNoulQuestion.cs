// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Represents a Noul question, which returns the probability that a yes or no statement is true.
/// </summary>
/// <remarks>
/// The answer is a <see cref="JevNoulAnswer"/>.
/// </remarks>
[Description("A Noul question: returns the probability, from 0 to 1, that the yes or no statement in instructions is true.")]
public sealed class JevNoulQuestion : JevQuestion
{
    /// <summary>
    /// Gets or sets optional descriptions of what a yes and a no mean.
    /// </summary>
    [JsonPropertyName("criteria")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Description("Optional descriptions of what a yes and a no mean.")]
    public JevNoulCriteria? Criteria { get; set; }
}
