// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Represents Jev's answer to one question of a <see cref="JevRequest"/>.
/// </summary>
/// <remarks>
/// The JSON <c>type</c> property matches the kind of the question: <c>choice</c> (<see cref="JevChoiceAnswer"/>),
/// <c>score</c> (<see cref="JevScoreAnswer"/>), or <c>noul</c> (<see cref="JevNoulAnswer"/>).
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(JevChoiceAnswer), "choice")]
[JsonDerivedType(typeof(JevScoreAnswer), "score")]
[JsonDerivedType(typeof(JevNoulAnswer), "noul")]
public abstract class JevAnswer
{
    // Only the three answer kinds that the Jev API returns can exist.
    private protected JevAnswer()
    {
    }
}
