// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.Hosting.Channels.Invocations;

[JsonSerializable(typeof(InvocationRequestModel))]
[JsonSerializable(typeof(InvocationResponseModel))]
[JsonSerializable(typeof(InvocationAwaitingInputModel))]
[JsonSerializable(typeof(InvocationContinuationModel))]
[JsonSerializable(typeof(InvocationErrorModel))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal sealed partial class InvocationsJsonContext : JsonSerializerContext;