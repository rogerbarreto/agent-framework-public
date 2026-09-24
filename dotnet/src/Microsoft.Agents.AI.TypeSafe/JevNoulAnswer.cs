// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Represents the answer to a <see cref="JevNoulQuestion"/>.
/// </summary>
[Description("The answer to a Noul question.")]
public sealed class JevNoulAnswer : JevAnswer
{
    /// <summary>
    /// Gets or sets the probability, from 0 to 1, that the answer is yes.
    /// </summary>
    [JsonPropertyName("noul")]
    [Description("The probability, from 0 to 1, that the answer is yes.")]
    public required double Noul { get; set; }
}
