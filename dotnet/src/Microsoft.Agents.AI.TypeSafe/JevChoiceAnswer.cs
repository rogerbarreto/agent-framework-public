// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Represents the answer to a <see cref="JevChoiceQuestion"/>.
/// </summary>
[Description("The answer to a Choice question.")]
public sealed class JevChoiceAnswer : JevAnswer
{
    /// <summary>
    /// Gets or sets the option with the highest probability.
    /// </summary>
    [JsonPropertyName("choice")]
    [Description("The option with the highest probability.")]
    public required string Choice { get; set; }

    /// <summary>
    /// Gets or sets how certain the answer is, from 0 to 1, derived from how the probability is spread across the options.
    /// </summary>
    [JsonPropertyName("confidence")]
    [Description("How certain the answer is, from 0 to 1. Treat low confidence as uncertain.")]
    public required double Confidence { get; set; }

    /// <summary>
    /// Gets or sets the probability of each option. The probabilities sum to 1.
    /// </summary>
    [JsonPropertyName("probabilities")]
    [Description("The probability of each option. The probabilities sum to 1.")]
    public required IReadOnlyDictionary<string, double> Probabilities { get; set; }
}
