// Copyright (c) Microsoft. All rights reserved.

using System;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Renderable artifact produced by <see cref="IIdentityLinker.BeginAsync"/>. Channels project this
/// onto their wire (one-time code message, OAuth redirect URL, MFA prompt, ...).
/// </summary>
/// <param name="ChallengeId">Stable id passed back into <see cref="IIdentityLinker.CompleteAsync"/>.</param>
/// <param name="Kind">Free-form kind discriminator ("url", "code", "mfa", ...).</param>
/// <param name="Url">Optional redirect URL for OAuth-style flows.</param>
/// <param name="Code">Optional human-presentable code for code-entry flows.</param>
/// <param name="UserPrompt">Optional natural-language instruction.</param>
public sealed record LinkChallenge(
    string ChallengeId,
    string Kind,
    Uri? Url = null,
    string? Code = null,
    string? UserPrompt = null);