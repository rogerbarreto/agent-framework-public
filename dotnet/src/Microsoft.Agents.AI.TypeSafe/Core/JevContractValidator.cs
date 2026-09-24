// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Checks requests before they are evaluated and responses before they are returned to the caller.
/// </summary>
/// <remarks>
/// Requests come from a model, so their errors are reported as <see cref="ArgumentException"/> messages that say how to
/// fix the arguments. Responses come from an evaluator, possibly a custom client with its own mappers, so their errors
/// are reported as <see cref="InvalidOperationException"/>; they point at an evaluator or mapper bug, not at the model.
/// </remarks>
internal static class JevContractValidator
{
    /// <summary>The most options that the Jev API accepts in one Choice question.</summary>
    public const int MaxChoiceOptions = 255;

    /// <summary>
    /// The fewest levels of a Score question. The API also accepts a single level, but the documentation requires two,
    /// and a one-level score always returns that level, so it is rejected as a request mistake.
    /// </summary>
    public const int MinScoreLevels = 2;

    /// <summary>The most levels that the Jev API accepts in one Score question.</summary>
    public const int MaxScoreLevels = 10;

    // Jev computes scores and probabilities in floating point, so values may exceed their bounds by rounding error.
    private const double Tolerance = 1e-6;

    public static void ValidateRequest(JevRequest request)
    {
        if (request.State.ValueKind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
        {
            Throw.ArgumentException(nameof(request), "The state must be a string, a JSON object, or a JSON array.");
        }

        if (request.Questions is null || request.Questions.Count == 0)
        {
            Throw.ArgumentException(nameof(request), "The request must contain at least one question.");
        }

        foreach (KeyValuePair<string, JevQuestion> entry in request.Questions)
        {
            string id = entry.Key;
            if (string.IsNullOrWhiteSpace(id))
            {
                Throw.ArgumentException(nameof(request), "Question IDs must not be empty.");
            }

            JevQuestion question = entry.Value;
            if (question is null)
            {
                Throw.ArgumentException(nameof(request), $"Question '{id}' is null.");
            }

            if (string.IsNullOrWhiteSpace(question.Instructions))
            {
                Throw.ArgumentException(nameof(request), $"Question '{id}' must have instructions.");
            }

            switch (question)
            {
                case JevChoiceQuestion choice:
                    int options = choice.Criteria?.Count ?? 0;
                    if (options is < 1 or > MaxChoiceOptions)
                    {
                        Throw.ArgumentException(nameof(request), $"Choice question '{id}' has {Count(options, "option")}; it needs from 1 to {MaxChoiceOptions}.");
                    }

                    foreach (string option in choice.Criteria!.Keys)
                    {
                        if (string.IsNullOrWhiteSpace(option))
                        {
                            Throw.ArgumentException(nameof(request), $"Choice question '{id}' has an empty option name.");
                        }
                    }

                    break;

                case JevScoreQuestion score:
                    int levels = score.Criteria?.Count ?? 0;
                    if (levels is < MinScoreLevels or > MaxScoreLevels)
                    {
                        Throw.ArgumentException(nameof(request), $"Score question '{id}' has {Count(levels, "level")}; it needs from {MinScoreLevels} to {MaxScoreLevels}.");
                    }

                    foreach (string level in score.Criteria!)
                    {
                        if (string.IsNullOrWhiteSpace(level))
                        {
                            Throw.ArgumentException(nameof(request), $"Score question '{id}' has an empty level description.");
                        }
                    }

                    break;
            }
        }
    }

    public static void ValidateResponse(JevRequest request, JevResponse response)
    {
        if (response.Answers is null)
        {
            Throw.InvalidOperationException("The Jev response has no answers.");
        }

        foreach (KeyValuePair<string, JevQuestion> entry in request.Questions)
        {
            string id = entry.Key;
            if (!response.Answers.TryGetValue(id, out JevAnswer? answer) || answer is null)
            {
                Throw.InvalidOperationException($"The Jev response has no answer for question '{id}'.");
            }

            switch (entry.Value, answer)
            {
                case (JevChoiceQuestion question, JevChoiceAnswer choice):
                    // A choice outside the options would reach the model as a value it never offered, which is how a
                    // mapper that converts between option names and a client's own values would fail silently.
                    if (choice.Choice is null || !question.Criteria.ContainsKey(choice.Choice))
                    {
                        Throw.InvalidOperationException($"The answer to question '{id}' chose '{choice.Choice}', which is not one of its options.");
                    }

                    EnsureInRange(choice.Confidence, 0, 1, id, "confidence");
                    EnsureProbabilities(choice.Probabilities, id, key => question.Criteria.ContainsKey(key), "one of its options");
                    break;

                case (JevScoreQuestion question, JevScoreAnswer score):
                    EnsureInRange(score.Score, 0, question.Criteria.Count - 1, id, "score");
                    EnsureInRange(score.Confidence, 0, 1, id, "confidence");

                    // Score answers key their probabilities and legend by level number ("0", "1", ...).
                    int levels = question.Criteria.Count;
                    bool IsLevel(string key) => int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int level) && level < levels;
                    string levelRange = $"a level number from 0 to {(levels - 1).ToString(CultureInfo.InvariantCulture)}";
                    EnsureProbabilities(score.Probabilities, id, IsLevel, levelRange);
                    EnsureKeys(score.Legend?.Keys, id, "legend", IsLevel, levelRange);
                    break;

                case (JevNoulQuestion, JevNoulAnswer noul):
                    EnsureInRange(noul.Noul, 0, 1, id, "noul");
                    break;

                default:
                    Throw.InvalidOperationException($"The answer to question '{id}' is a {KindOf(answer)} answer, but the question is a {KindOf(entry.Value)} question.");
                    break;
            }
        }
    }

    /// <summary>
    /// Checks that every probability is in range and is keyed by something the question offered. A mapper that fills
    /// probabilities with a client's own labels would otherwise show the model options it never asked about.
    /// </summary>
    private static void EnsureProbabilities(IReadOnlyDictionary<string, double>? probabilities, string id, Func<string, bool> isValidKey, string expected)
    {
        if (probabilities is null)
        {
            Throw.InvalidOperationException($"The answer to question '{id}' has no probabilities.");
        }

        EnsureKeys(probabilities.Keys, id, "probabilities", isValidKey, expected);
        foreach (KeyValuePair<string, double> probability in probabilities)
        {
            EnsureInRange(probability.Value, 0, 1, id, $"probability '{probability.Key}'");
        }
    }

    private static void EnsureKeys(IEnumerable<string>? keys, string id, string field, Func<string, bool> isValidKey, string expected)
    {
        foreach (string key in keys ?? [])
        {
            if (!isValidKey(key))
            {
                Throw.InvalidOperationException($"The answer to question '{id}' has {field} for '{key}', which is not {expected}.");
            }
        }
    }

    private static void EnsureInRange(double value, double min, double max, string id, string field)
    {
        // NaN and infinity fail these comparisons too; they cannot be written as JSON at all.
        if (!(value >= min - Tolerance && value <= max + Tolerance))
        {
            Throw.InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "The answer to question '{0}' has {1} {2}, which is outside the range {3} to {4}.",
                id,
                field,
                value,
                min,
                max));
        }
    }

    private static string Count(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count.ToString(CultureInfo.InvariantCulture)} {noun}s";

    private static string KindOf(object value) =>
        value switch
        {
            JevChoiceQuestion or JevChoiceAnswer => "Choice",
            JevScoreQuestion or JevScoreAnswer => "Score",
            JevNoulQuestion or JevNoulAnswer => "Noul",
            _ => value.GetType().Name,
        };
}
