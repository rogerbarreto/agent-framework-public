// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.Hosting.Channels.Responses;

[JsonSerializable(typeof(ResponsesRequestModel))]
[JsonSerializable(typeof(ResponsesResponseModel))]
[JsonSerializable(typeof(ResponsesStreamResponseEvent))]
[JsonSerializable(typeof(ResponsesStreamTextDeltaEvent))]
[JsonSerializable(typeof(ResponsesErrorModel))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal sealed partial class ResponsesJsonContext : JsonSerializerContext;
