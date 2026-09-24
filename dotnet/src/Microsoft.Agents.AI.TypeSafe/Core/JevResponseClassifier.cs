// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ClientModel.Primitives;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Classifies System One responses: any 2xx status is a success, and 529 is retriable in addition to the statuses
/// that <see cref="PipelineMessageClassifier.Default"/> retries (408, 429, 500, 502, 503, and 504).
/// </summary>
/// <remarks>
/// TypeSafe answers 529 when it is overloaded and documents it as transient, but the default classifier treats it as a
/// permanent error. Every other case, including network failures, is left to the default classifier.
/// </remarks>
internal sealed class JevResponseClassifier : PipelineMessageClassifier
{
    private const int OverloadedStatus = 529;

    public static JevResponseClassifier Instance { get; } = new();

    public override bool TryClassify(PipelineMessage message, out bool isError)
    {
        if (message.Response is not { } response)
        {
            isError = false;
            return false;
        }

        isError = response.Status is < 200 or >= 300;
        return true;
    }

    public override bool TryClassify(PipelineMessage message, Exception? exception, out bool isRetriable)
    {
        isRetriable = exception is null && message.Response?.Status == OverloadedStatus;
        return isRetriable;
    }
}
