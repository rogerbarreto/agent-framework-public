// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.Hosting.Channels.Invocations;

/// <summary>Inbound payload for <c>POST {Path}/invoke</c>.</summary>
internal sealed class InvocationRequestModel
{
    [JsonPropertyName("input")]
    public object? Input { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, object?>? Attributes { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }

    [JsonPropertyName("background")]
    public bool Background { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("isolation_key")]
    public string? IsolationKey { get; set; }
}

/// <summary>Successful response envelope for a completed run.</summary>
internal sealed class InvocationResponseModel
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "completed";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("continuation_token")]
    public string? ContinuationToken { get; set; }
}

/// <summary>Awaiting-input envelope when a workflow target paused on a <c>RequestInfoEvent</c>.</summary>
internal sealed class InvocationAwaitingInputModel
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "awaiting_input";

    [JsonPropertyName("request")]
    public object? Request { get; set; }

    [JsonPropertyName("resume_token")]
    public string? ResumeToken { get; set; }
}

/// <summary>Queued / running envelope for background runs.</summary>
internal sealed class InvocationContinuationModel
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "queued";

    [JsonPropertyName("continuation_token")]
    public string? ContinuationToken { get; set; }
}

/// <summary>Error envelope.</summary>
internal sealed class InvocationErrorModel
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "failed";

    [JsonPropertyName("error_code")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}