// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.Hosting.Channels.Responses;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ResponsesRequestModel))]
[JsonSerializable(typeof(ResponsesResponseModel))]
[JsonSerializable(typeof(ResponsesStreamResponseEvent))]
[JsonSerializable(typeof(ResponsesStreamOutputItemEvent))]
[JsonSerializable(typeof(ResponsesStreamContentPartEvent))]
[JsonSerializable(typeof(ResponsesStreamTextDeltaEvent))]
[JsonSerializable(typeof(ResponsesStreamTextDoneEvent))]
[JsonSerializable(typeof(ResponsesErrorModel))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal sealed partial class ResponsesJsonContext : JsonSerializerContext;
