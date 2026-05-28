// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.Hosting.Channels.Telegram;

internal sealed class TelegramUpdate
{
    [JsonPropertyName("update_id")] public long UpdateId { get; set; }
    [JsonPropertyName("message")] public TelegramMessage? Message { get; set; }
    [JsonPropertyName("channel_post")] public TelegramMessage? ChannelPost { get; set; }
}

internal sealed class TelegramMessage
{
    [JsonPropertyName("message_id")] public long MessageId { get; set; }
    [JsonPropertyName("from")] public TelegramUser? From { get; set; }
    [JsonPropertyName("chat")] public TelegramChat? Chat { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("entities")] public TelegramMessageEntity[]? Entities { get; set; }
}

internal sealed class TelegramUser
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("is_bot")] public bool IsBot { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("language_code")] public string? LanguageCode { get; set; }
}

internal sealed class TelegramChat
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }   // "private" | "group" | "supergroup" | "channel"
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
}

internal sealed class TelegramMessageEntity
{
    [JsonPropertyName("type")] public string? Type { get; set; }   // "bot_command", "mention", ...
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("length")] public int Length { get; set; }
}

internal sealed class TelegramSendMessage
{
    [JsonPropertyName("chat_id")] public long ChatId { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("parse_mode")] public string? ParseMode { get; set; }
}

internal sealed class TelegramGetUpdatesResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("result")] public TelegramUpdate[]? Result { get; set; }
}

internal sealed class TelegramGetMeResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("result")] public TelegramUser? Result { get; set; }
}

internal sealed class TelegramSetMyCommandsRequest
{
    [JsonPropertyName("commands")] public TelegramBotCommand[] Commands { get; set; } = [];
}

internal sealed class TelegramBotCommand
{
    [JsonPropertyName("command")] public string Command { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
}