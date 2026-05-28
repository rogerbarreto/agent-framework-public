// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels.Telegram;

internal sealed class TelegramApiClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public TelegramApiClient(HttpClient http, string botToken)
    {
        this._http = Throw.IfNull(http);
        Throw.IfNullOrEmpty(botToken);
        this._baseUrl = $"https://api.telegram.org/bot{botToken}";
    }

    public async Task<TelegramUser?> GetMeAsync(CancellationToken cancellationToken)
    {
        var response = await this._http.GetFromJsonAsync(
            new Uri($"{this._baseUrl}/getMe"),
            TelegramJsonContext.Default.TelegramGetMeResponse,
            cancellationToken).ConfigureAwait(false);
        return response?.Ok == true ? response.Result : null;
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var url = $"{this._baseUrl}/getUpdates?offset={offset}&timeout={timeoutSeconds}";
        var response = await this._http.GetFromJsonAsync(
            new Uri(url),
            TelegramJsonContext.Default.TelegramGetUpdatesResponse,
            cancellationToken).ConfigureAwait(false);
        return response?.Ok == true && response.Result is not null ? response.Result : [];
    }

    public async Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        Throw.IfNull(text);
        var payload = new TelegramSendMessage { ChatId = chatId, Text = text };
        using var http = await this._http.PostAsJsonAsync(
            new Uri($"{this._baseUrl}/sendMessage"),
            payload,
            TelegramJsonContext.Default.TelegramSendMessage,
            cancellationToken).ConfigureAwait(false);
        http.EnsureSuccessStatusCode();
    }

    public async Task SetMyCommandsAsync(IReadOnlyList<ChannelCommand> commands, CancellationToken cancellationToken)
    {
        Throw.IfNull(commands);
        if (commands.Count == 0) { return; }
        var payload = new TelegramSetMyCommandsRequest
        {
            Commands = ToCommands(commands),
        };
        using var http = await this._http.PostAsJsonAsync(
            new Uri($"{this._baseUrl}/setMyCommands"),
            payload,
            TelegramJsonContext.Default.TelegramSetMyCommandsRequest,
            cancellationToken).ConfigureAwait(false);
        http.EnsureSuccessStatusCode();
    }

    private static TelegramBotCommand[] ToCommands(IReadOnlyList<ChannelCommand> commands)
    {
        var arr = new TelegramBotCommand[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            arr[i] = new TelegramBotCommand
            {
                Command = commands[i].Name.TrimStart('/'),
                Description = commands[i].Description,
            };
        }
        return arr;
    }
}