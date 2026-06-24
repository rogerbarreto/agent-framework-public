// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Hosting.Channels.Responses;

/// <summary>
/// Parses the Responses request <c>input</c> field (string or input-item array) into a
/// <see cref="ChatMessage"/> list. Channel owns protocol parsing per ADR-0027.
/// </summary>
internal static class ResponsesParsing
{
    public static IReadOnlyList<ChatMessage> MessagesFromInput(JsonElement? input, string? instructions)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            messages.Add(new ChatMessage(ChatRole.System, instructions));
        }

        if (input is null)
        {
            return messages;
        }

        var el = input.Value;
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                messages.Add(new ChatMessage(ChatRole.User, el.GetString() ?? string.Empty));
                break;

            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    AppendItem(messages, item);
                }
                break;
        }

        return messages;
    }

    private static void AppendItem(List<ChatMessage> messages, JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            messages.Add(new ChatMessage(ChatRole.User, item.GetString() ?? string.Empty));
            return;
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var role = item.TryGetProperty("role", out var roleEl) ? roleEl.GetString() : "user";
        var text = ExtractText(item);
        if (text.Length == 0)
        {
            return;
        }

        messages.Add(new ChatMessage(MapRole(role), text));
    }

    private static string ExtractText(JsonElement item)
    {
        if (item.TryGetProperty("content", out var content))
        {
            if (content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }

            if (content.ValueKind == JsonValueKind.Array)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(t.GetString());
                    }
                }
                return sb.ToString();
            }
        }

        if (item.TryGetProperty("text", out var directText) && directText.ValueKind == JsonValueKind.String)
        {
            return directText.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static ChatRole MapRole(string? role) => role switch
    {
        "assistant" => ChatRole.Assistant,
        "system" or "developer" => ChatRole.System,
        "tool" => ChatRole.Tool,
        _ => ChatRole.User,
    };
}
