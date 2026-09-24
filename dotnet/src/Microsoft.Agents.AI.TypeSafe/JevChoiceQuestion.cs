// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Represents a Choice question, which picks exactly one option from a set that the caller defines.
/// </summary>
/// <remarks>
/// The answer is a <see cref="JevChoiceAnswer"/>.
/// </remarks>
[Description("A Choice question: picks exactly one option from criteria.")]
public sealed class JevChoiceQuestion : JevQuestion
{
    /// <summary>
    /// Gets or sets the options, keyed by the option name that the answer returns.
    /// </summary>
    /// <remarks>
    /// Each value describes when the option applies, or is <see langword="null"/> when the name is enough.
    /// Jev accepts from 1 to 255 options.
    /// </remarks>
    [JsonPropertyName("criteria")]
    [Description("The options to choose from, keyed by the option name that the answer returns. Each value describes when the option applies. From 1 to 255 options.")]
    public required IReadOnlyDictionary<string, string?> Criteria { get; set; }
}
