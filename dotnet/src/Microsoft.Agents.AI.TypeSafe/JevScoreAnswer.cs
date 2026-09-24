// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Represents the answer to a <see cref="JevScoreQuestion"/>.
/// </summary>
[Description("The answer to a Score question.")]
public sealed class JevScoreAnswer : JevAnswer
{
    /// <summary>
    /// Gets or sets the score, weighted by the probability of each level. It can fall between levels.
    /// </summary>
    [JsonPropertyName("score")]
    [Description("The score, weighted by the probability of each level. It can fall between levels.")]
    public required double Score { get; set; }

    /// <summary>
    /// Gets or sets how certain the answer is, from 0 to 1, derived from how the probability is spread across the levels.
    /// </summary>
    [JsonPropertyName("confidence")]
    [Description("How certain the answer is, from 0 to 1. Treat low confidence as uncertain.")]
    public required double Confidence { get; set; }

    /// <summary>
    /// Gets or sets the description of each level, keyed by the level number.
    /// </summary>
    [JsonPropertyName("legend")]
    [Description("The description of each level, keyed by the level number.")]
    public required IReadOnlyDictionary<string, string> Legend { get; set; }

    /// <summary>
    /// Gets or sets the probability of each level, keyed by the level number. The probabilities sum to 1.
    /// </summary>
    [JsonPropertyName("probabilities")]
    [Description("The probability of each level, keyed by the level number. The probabilities sum to 1.")]
    public required IReadOnlyDictionary<string, double> Probabilities { get; set; }
}
