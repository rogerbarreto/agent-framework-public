// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels.Telegram;

/// <summary>
/// Telegram Bot channel. Supports two transports: long-poll <see cref="TelegramTransport.Polling"/>
/// via an <see cref="Microsoft.Extensions.Hosting.IHostedService"/> loop, or webhook
/// <see cref="TelegramTransport.Webhook"/> via <c>POST {Path}/webhook</c>. Implements
/// <see cref="IChannelPush"/> for cross-channel response delivery.
/// </summary>
public sealed class TelegramChannel : Channel, IChannelPush
{
    private readonly TelegramChannelOptions _options;
    private TelegramApiClient? _api;
    private string? _botUsername;

    /// <summary>Initializes a new instance.</summary>
    public TelegramChannel(TelegramChannelOptions options)
    {
        this._options = Throw.IfNull(options);
        if (string.IsNullOrEmpty(this._options.BotToken))
        {
            throw new ArgumentException("BotToken is required.", nameof(options));
        }
    }

    /// <inheritdoc />
    public override string Name => "telegram";

    /// <inheritdoc />
    public override string Path => this._options.Path;

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpClient<TelegramApiClient>();
    }

    /// <inheritdoc />
    public override ChannelContribution Contribute(IChannelContext context)
    {
        Throw.IfNull(context);

        var httpClient = context.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TelegramApiClient));
        this._api = new TelegramApiClient(httpClient, this._options.BotToken);

        var contribution = new ChannelContribution
        {
            Commands = [.. this._options.Commands],
            OnStartup = ct => this.OnStartupAsync(context, ct),
        };

        if (this._options.Transport == TelegramTransport.Webhook)
        {
            contribution = contribution with
            {
                Routes =
                [
                    endpoints => endpoints.MapPost("/webhook", (HttpContext http) => this.HandleWebhookAsync(context, http)),
                ],
            };
        }

        return contribution;
    }

    /// <inheritdoc />
    public async ValueTask PushAsync(ChannelPushContext context, HostedRunResult payload, CancellationToken cancellationToken)
    {
        Throw.IfNull(context);
        Throw.IfNull(payload);
        if (this._api is null) { throw new InvalidOperationException("TelegramChannel.Contribute was not invoked before PushAsync."); }
        if (!long.TryParse(context.Destination.NativeId, out var chatId))
        {
            throw new InvalidOperationException($"Destination NativeId '{context.Destination.NativeId}' is not a valid Telegram chat id.");
        }
        var text = ExtractText(payload, context.IsEcho ? context.OriginatingRequest.Input?.ToString() : null);
        if (string.IsNullOrEmpty(text)) { return; }
        await this._api.SendMessageAsync(chatId, text, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask OnStartupAsync(IChannelContext context, CancellationToken cancellationToken)
    {
        if (this._api is null) { return; }

        var me = await this._api.GetMeAsync(cancellationToken).ConfigureAwait(false);
        this._botUsername = me?.Username;

        if (this._options.RegisterNativeCommands && this._options.Commands.Count > 0)
        {
            await this._api.SetMyCommandsAsync([.. this._options.Commands], cancellationToken).ConfigureAwait(false);
        }

        if (this._options.Transport == TelegramTransport.Polling)
        {
            // Start the polling loop on a background task. Stops when the application shuts down.
            var logger = context.Services.GetRequiredService<ILoggerFactory>().CreateLogger<TelegramChannel>();
            _ = Task.Run(() => this.PollingLoopAsync(context, logger, cancellationToken), cancellationToken);
        }
    }

    private async Task PollingLoopAsync(IChannelContext context, ILogger<TelegramChannel> logger, CancellationToken cancellationToken)
    {
        if (this._api is null) { return; }
        var offset = 0L;
        var timeoutSeconds = Math.Max(1, (int)this._options.PollingTimeout.TotalSeconds);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var updates = await this._api.GetUpdatesAsync(offset, timeoutSeconds, cancellationToken).ConfigureAwait(false);
                for (var i = 0; i < updates.Count; i++)
                {
                    var update = updates[i];
                    offset = Math.Max(offset, update.UpdateId + 1);
                    await this.HandleUpdateAsync(context, update, replyHandler: null, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telegram polling loop iteration failed; retrying in 5s.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task HandleWebhookAsync(IChannelContext context, HttpContext http)
    {
        TelegramUpdate? update;
        try
        {
            update = await JsonSerializer.DeserializeAsync(http.Request.Body, TelegramJsonContext.Default.TelegramUpdate, http.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (update is null) { http.Response.StatusCode = StatusCodes.Status204NoContent; return; }

        await this.HandleUpdateAsync(context, update, replyHandler: async (text) =>
        {
            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.ContentType = "application/json; charset=utf-8";
            // Per Telegram webhook spec, the response body MAY be a method invocation. We just ack here.
            await http.Response.WriteAsync("{\"ok\":true}", http.RequestAborted).ConfigureAwait(false);
            // The actual reply goes via sendMessage; this keeps the wire simple.
            if (!string.IsNullOrEmpty(text) && this._api is not null && update.Message?.Chat is not null)
            {
                await this._api.SendMessageAsync(update.Message.Chat.Id, text!, http.RequestAborted).ConfigureAwait(false);
            }
        }, http.RequestAborted).ConfigureAwait(false);
    }

    private async Task HandleUpdateAsync(IChannelContext context, TelegramUpdate update, Func<string?, Task>? replyHandler, CancellationToken cancellationToken)
    {
        var message = update.Message ?? update.ChannelPost;
        if (message?.From is null || message.Chat is null || string.IsNullOrEmpty(message.Text)) { return; }

        var isGroup = message.Chat.Type is "group" or "supergroup";
        if (isGroup && !this.AcceptInGroupChat(message))
        {
            return;
        }

        var identity = new ChannelIdentity(this.Name, message.From.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            Attributes = BuildIdentityAttributes(message.From),
        };

        var conversationContext = new ConversationContext(message.Chat.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), isGroup);

        var auth = await context.AuthorizeAsync(identity, new AuthorizationRequest
        {
            RequireLink = this._options.RequireLink,
            ConversationContext = conversationContext,
        }, cancellationToken).ConfigureAwait(false);

        switch (auth)
        {
            case AuthorizationOutcome.Denied:
                return;

            case AuthorizationOutcome.LinkRequired linkRequired:
                await this.SendLinkChallengeAsync(message, linkRequired.Challenge, isGroup, cancellationToken).ConfigureAwait(false);
                return;
        }

        if (auth is not AuthorizationOutcome.Allowed allowed) { return; }

        var isolationKey = this.DeriveIsolationKey(allowed.IsolationKey, message.Chat.Id);

        var request = new ChannelRequest
        {
            Channel = this.Name,
            Operation = "message.create",
            Input = message.Text,
            Identity = identity,
            ConversationId = message.Chat.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Session = new ChannelSession
            {
                IsolationKey = isolationKey,
                ConversationId = message.Chat.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
        };

        if (this._options.RunHook is not null)
        {
            var hookContext = new ChannelRunHookContext { Target = context.Host, ProtocolRequest = update };
            request = await this._options.RunHook.OnRequestAsync(request, hookContext, cancellationToken).ConfigureAwait(false);
        }

        await context.StateStore.RecordLastSeenAsync(isolationKey, identity, request.ConversationId, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

        var result = await context.RunAsync(request, cancellationToken).ConfigureAwait(false);
        var text = ExtractText(result, null);

        if (replyHandler is not null)
        {
            await replyHandler(text).ConfigureAwait(false);
        }
        else if (!string.IsNullOrEmpty(text) && this._api is not null)
        {
            await this._api.SendMessageAsync(message.Chat.Id, text!, cancellationToken).ConfigureAwait(false);
        }

        await context.ScheduleResponseAsync(result, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendLinkChallengeAsync(TelegramMessage message, LinkChallenge challenge, bool isGroup, CancellationToken cancellationToken)
    {
        if (this._api is null || message.Chat is null) { return; }

        var prompt = challenge.UserPrompt ?? $"Please complete the link ceremony (code: {challenge.Code}).";

        // Group-safety: never post the challenge into a group conversation. Redirect to the user's DM.
        if (isGroup && message.From is not null)
        {
            try
            {
                await this._api.SendMessageAsync(message.From.Id, prompt, cancellationToken).ConfigureAwait(false);
                await this._api.SendMessageAsync(message.Chat.Id, "I've sent you a private message with the link instructions.", cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                await this._api.SendMessageAsync(message.Chat.Id, "I need a private conversation with you first. Open a chat with me and try again.", cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        await this._api.SendMessageAsync(message.Chat.Id, prompt, cancellationToken).ConfigureAwait(false);
    }

    private bool AcceptInGroupChat(TelegramMessage message)
    {
        var hasMention = MentionsBot(message, this._botUsername);
        var hasCommand = HasCommand(message);

        return this._options.AcceptInGroup switch
        {
            AcceptInGroup.All => true,
            AcceptInGroup.MentionOnly => hasMention,
            AcceptInGroup.CommandOnly => hasCommand,
            AcceptInGroup.MentionOrCommand => hasMention || hasCommand,
            _ => false,
        };
    }

    private string DeriveIsolationKey(string userIsolationKey, long chatId) =>
        this._options.ConversationScope switch
        {
            ConversationScope.PerUser => userIsolationKey,
            ConversationScope.PerConversation => $"_conv:{this.Name}:{chatId}",
            _ => $"{userIsolationKey}:{chatId}",
        };

    private static ImmutableDictionary<string, string> BuildIdentityAttributes(TelegramUser user)
    {
        var b = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        if (user.Username is not null) { b["username"] = user.Username; }
        if (user.FirstName is not null) { b["first_name"] = user.FirstName; }
        if (user.LanguageCode is not null) { b["language_code"] = user.LanguageCode; }
        return b.ToImmutable();
    }

    private static bool MentionsBot(TelegramMessage message, string? botUsername)
    {
        if (string.IsNullOrEmpty(botUsername) || message.Entities is null || message.Text is null) { return false; }
        var needle = "@" + botUsername;
        for (var i = 0; i < message.Entities.Length; i++)
        {
            var entity = message.Entities[i];
            if (entity.Type == "mention" && entity.Offset + entity.Length <= message.Text.Length)
            {
                var seg = message.Text.AsSpan(entity.Offset, entity.Length);
                if (seg.Equals(needle.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool HasCommand(TelegramMessage message)
    {
        if (message.Entities is null) { return false; }
        for (var i = 0; i < message.Entities.Length; i++)
        {
            if (message.Entities[i].Type == "bot_command") { return true; }
        }
        return false;
    }

    private static string? ExtractText(HostedRunResult result, string? fallback) => result.ResultObject switch
    {
        AgentResponse response => response.Text,
        AgentResponseUpdate update => update.Text,
        WorkflowRunResult workflow => RenderWorkflow(workflow),
        string s => s,
        _ => fallback ?? result.ResultObject?.ToString(),
    };

    private static string RenderWorkflow(WorkflowRunResult workflow) => workflow.Status switch
    {
        WorkflowRunStatus.AwaitingInput => "Awaiting input...",
        WorkflowRunStatus.Failed => $"Workflow failed: {workflow.Error ?? "unknown error"}",
        _ => workflow.Outputs.Count == 0 ? string.Empty : string.Join(System.Environment.NewLine, workflow.Outputs),
    };
}