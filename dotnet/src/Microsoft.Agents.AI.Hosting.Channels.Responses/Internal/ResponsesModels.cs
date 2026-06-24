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
}

/// <summary>Outbound Responses object (non-streaming + terminal stream payload).</summary>
internal sealed class ResponsesResponseModel
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("object")] public string Object { get; set; } = "response";
    [JsonPropertyName("created_at")] public long CreatedAt { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "completed";
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("output")] public List<ResponsesOutputMessage> Output { get; set; } = [];
    [JsonPropertyName("usage")] public ResponsesUsageModel? Usage { get; set; }
}

internal sealed class ResponsesOutputMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "message";
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; set; } = "assistant";
    [JsonPropertyName("status")] public string Status { get; set; } = "completed";
    [JsonPropertyName("content")] public List<ResponsesOutputText> Content { get; set; } = [];
}

internal sealed class ResponsesOutputText
{
    [JsonPropertyName("type")] public string Type { get; set; } = "output_text";
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("annotations")] public List<object> Annotations { get; set; } = [];
}

internal sealed class ResponsesUsageModel
{
    [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }
    [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
    [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
}

/// <summary>SSE payload for response.created / response.completed.</summary>
internal sealed class ResponsesStreamResponseEvent
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("response")] public ResponsesResponseModel Response { get; set; } = new();
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
