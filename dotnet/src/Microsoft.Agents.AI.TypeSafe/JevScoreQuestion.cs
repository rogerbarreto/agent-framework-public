// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Represents a Score question, which rates the state against ordered levels that the caller defines.
/// </summary>
/// <remarks>
/// The answer is a <see cref="JevScoreAnswer"/>, whose score is weighted by probability and can fall between levels.
/// </remarks>
[Description("A Score question: rates the state against ordered levels in criteria.")]
public sealed class JevScoreQuestion : JevQuestion
{
    /// <summary>
    /// Gets or sets the level descriptions, ordered from level 0 (the lowest) to the highest level.
    /// </summary>
    /// <remarks>
    /// A Score needs from 2 to 10 levels.
    /// </remarks>
    [JsonPropertyName("criteria")]
    [Description("The level descriptions, ordered from level 0 (the lowest) to the highest level. From 2 to 10 levels.")]
    public required IReadOnlyList<string> Criteria { get; set; }
}
