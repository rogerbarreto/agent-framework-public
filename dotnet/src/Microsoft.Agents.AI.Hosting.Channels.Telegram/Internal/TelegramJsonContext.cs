// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.Hosting.Channels.Telegram;

[JsonSerializable(typeof(TelegramUpdate))]
[JsonSerializable(typeof(TelegramMessage))]
[JsonSerializable(typeof(TelegramSendMessage))]
[JsonSerializable(typeof(TelegramGetUpdatesResponse))]
[JsonSerializable(typeof(TelegramGetMeResponse))]
[JsonSerializable(typeof(TelegramSetMyCommandsRequest))]
internal sealed partial class TelegramJsonContext : JsonSerializerContext;