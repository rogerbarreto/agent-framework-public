// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.Hosting.Channels.Responses;

/// <summary>Inbound OpenAI Responses request body (subset).</summary>
internal sealed class ResponsesRequestModel
{
    [JsonPropertyName("input")] public JsonElement? Input { get; set; }
    [JsonPropertyName("stream")] public bool Stream { get; set; }
    [JsonPropertyName("previous_response_id")] public string? PreviousResponseId { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("instructions")] public string? Instructions { get; set; }
    [JsonPropertyName("metadata")] public Dictionary<string, object?>? Metadata { get; set; }

    // Generation-control fields remapped onto ChatOptions (stripped by the default run hook).
    [JsonPropertyName("temperature")] public double? Temperature { get; set; }
    [JsonPropertyName("top_p")] public double? TopP { get; set; }
    [JsonPropertyName("max_output_tokens")] public int? MaxOutputTokens { get; set; }
    [JsonPropertyName("parallel_tool_calls")] public bool? ParallelToolCalls { get; set; }

    // Caller identity: OpenAI Responses replaced `user` with `safety_identifier`.
    [JsonPropertyName("safety_identifier")] public string? SafetyIdentifier { get; set; }
    [JsonPropertyName("user")] public string? User { get; set; }
}

/// <summary>Outbound Responses object (non-streaming + terminal stream payload).</summary>
internal sealed class ResponsesResponseModel
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("object")] public string Object { get; set; } = "response";
    [JsonPropertyName("created_at")] public long CreatedAt { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "completed";
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("output")] public List<ResponsesOutputItem> Output { get; set; } = [];
    [JsonPropertyName("usage")] public ResponsesUsageModel? Usage { get; set; }
    [JsonPropertyName("error")] public ResponsesErrorBody? Error { get; set; }
}

/// <summary>
/// Unified Responses output item. The <c>type</c> discriminates which fields apply: <c>message</c>
/// (role/content), <c>function_call</c> (call_id/name/arguments), <c>function_call_output</c>
/// (call_id/output), or <c>reasoning</c> (summary). Unset fields are omitted when serialized.
/// </summary>
internal sealed class ResponsesOutputItem
{
    [JsonPropertyName("type")] public string Type { get; set; } = "message";
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string? Status { get; set; }

    // message
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("content")] public List<ResponsesOutputText>? Content { get; set; }

    // function_call / function_call_output
    [JsonPropertyName("call_id")] public string? CallId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("arguments")] public string? Arguments { get; set; }
    [JsonPropertyName("output")] public string? Output { get; set; }

    // reasoning
    [JsonPropertyName("summary")] public List<ResponsesReasoningSummary>? Summary { get; set; }
}

internal sealed class ResponsesOutputText
{
    [JsonPropertyName("type")] public string Type { get; set; } = "output_text";
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("annotations")] public List<object> Annotations { get; set; } = [];
}

internal sealed class ResponsesReasoningSummary
{
    [JsonPropertyName("type")] public string Type { get; set; } = "summary_text";
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
}

internal sealed class ResponsesUsageModel
{
    [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }
    [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
    [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
}

/// <summary>SSE payload for response.created / response.completed / response.failed.</summary>
internal sealed class ResponsesStreamResponseEvent
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("response")] public ResponsesResponseModel Response { get; set; } = new();
}

/// <summary>SSE payload for response.output_item.added / response.output_item.done.</summary>
internal sealed class ResponsesStreamOutputItemEvent
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("output_index")] public int OutputIndex { get; set; }
    [JsonPropertyName("item")] public ResponsesOutputItem Item { get; set; } = new();
}

/// <summary>SSE payload for response.content_part.added / response.content_part.done.</summary>
internal sealed class ResponsesStreamContentPartEvent
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("item_id")] public string ItemId { get; set; } = string.Empty;
    [JsonPropertyName("output_index")] public int OutputIndex { get; set; }
    [JsonPropertyName("content_index")] public int ContentIndex { get; set; }
    [JsonPropertyName("part")] public ResponsesOutputText Part { get; set; } = new();
}

/// <summary>SSE payload for response.output_text.delta.</summary>
internal sealed class ResponsesStreamTextDeltaEvent
{
    [JsonPropertyName("type")] public string Type { get; set; } = "response.output_text.delta";
    [JsonPropertyName("item_id")] public string ItemId { get; set; } = string.Empty;
    [JsonPropertyName("output_index")] public int OutputIndex { get; set; }
    [JsonPropertyName("content_index")] public int ContentIndex { get; set; }
    [JsonPropertyName("delta")] public string Delta { get; set; } = string.Empty;
}

/// <summary>SSE payload for response.output_text.done.</summary>
internal sealed class ResponsesStreamTextDoneEvent
{
    [JsonPropertyName("type")] public string Type { get; set; } = "response.output_text.done";
    [JsonPropertyName("item_id")] public string ItemId { get; set; } = string.Empty;
    [JsonPropertyName("output_index")] public int OutputIndex { get; set; }
    [JsonPropertyName("content_index")] public int ContentIndex { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
}

/// <summary>Error envelope.</summary>
internal sealed class ResponsesErrorModel
{
    [JsonPropertyName("error")] public ResponsesErrorBody Error { get; set; } = new();
}

internal sealed class ResponsesErrorBody
{
    [JsonPropertyName("type")] public string Type { get; set; } = "invalid_request_error";
    [JsonPropertyName("message")] public string? Message { get; set; }
}
