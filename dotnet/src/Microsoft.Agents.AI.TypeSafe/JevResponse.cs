// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Represents the result of a Jev evaluation: one typed answer per question of the <see cref="JevRequest"/>.
/// </summary>
/// <remarks>
/// This type mirrors the body of a TypeSafe System One API response. Its JSON form is the result of the
/// <see cref="AIFunction"/> that <see cref="JevAIToolBuilder"/> creates.
/// </remarks>
public sealed class JevResponse
{
    /// <summary>
    /// Gets or sets the model that performed the evaluation, when the evaluator reports it.
    /// </summary>
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Description("The model that performed the evaluation.")]
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the answers, keyed by the ID of the question they answer.
    /// </summary>
    [JsonPropertyName("answers")]
    [Description("One answer per question, keyed by the question ID.")]
    public required IReadOnlyDictionary<string, JevAnswer> Answers { get; set; }

    /// <summary>
    /// Gets or sets the token usage of the evaluation, when the evaluator reports it.
    /// </summary>
    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Description("The token usage of the evaluation.")]
    public JevUsage? Usage { get; set; }
}
