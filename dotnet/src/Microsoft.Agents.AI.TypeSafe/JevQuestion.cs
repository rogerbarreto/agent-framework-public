// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Represents one typed question that Jev answers about the state of a <see cref="JevRequest"/>.
/// </summary>
/// <remarks>
/// The JSON <c>type</c> property selects the question kind: <c>choice</c> (<see cref="JevChoiceQuestion"/>),
/// <c>score</c> (<see cref="JevScoreQuestion"/>), or <c>noul</c> (<see cref="JevNoulQuestion"/>).
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(JevChoiceQuestion), "choice")]
[JsonDerivedType(typeof(JevScoreQuestion), "score")]
[JsonDerivedType(typeof(JevNoulQuestion), "noul")]
public abstract class JevQuestion
{
    // Only the three question kinds that the Jev API accepts can exist.
    private protected JevQuestion()
    {
    }

    /// <summary>
    /// Gets or sets what Jev should decide, rate, or check.
    /// </summary>
    [JsonPropertyName("instructions")]
    [Description("What Jev should decide, rate, or check. Describe the idea rather than exact words, because Jev matches on meaning.")]
    public required string Instructions { get; set; }
}
