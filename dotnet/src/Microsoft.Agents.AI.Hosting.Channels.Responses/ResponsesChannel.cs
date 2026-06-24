// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels.Responses;

/// <summary>
/// OpenAI Responses-shaped channel. Mounts a single <c>POST {Path}</c> endpoint that accepts a Responses
/// request body and returns either a Responses JSON object (<c>stream=false</c>, default) or a
/// Server-Sent-Events stream (<c>stream=true</c>). The channel owns protocol parsing and rendering; the host
/// owns target invocation and session resolution.
/// </summary>
public sealed class ResponsesChannel : Channel
{
    private readonly ResponsesChannelOptions _options;

    /// <summary>Initializes a new instance.</summary>
    public ResponsesChannel(ResponsesChannelOptions options)
    {
        this._options = Throw.IfNull(options);
    }

    /// <inheritdoc />
    public override string Name => "responses";

    /// <inheritdoc />
    public override string Path => this._options.Path;

    /// <inheritdoc />
    public override ChannelContribution Contribute(IChannelContext context)
    {
        Throw.IfNull(context);
        return new ChannelContribution
        {
            Routes = [endpoints => endpoints.MapPost("/", (HttpContext http) => this.HandleAsync(context, http))],
        };
    }

    private async Task HandleAsync(IChannelContext context, HttpContext http)
    {
        ResponsesRequestModel? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync(http.Request.Body, ResponsesJsonContext.Default.ResponsesRequestModel, http.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            await WriteErrorAsync(http, StatusCodes.Status400BadRequest, ex.Message).ConfigureAwait(false);
            return;
        }

        if (body is null)
        {
            await WriteErrorAsync(http, StatusCodes.Status400BadRequest, "Request body is required.").ConfigureAwait(false);
            return;
        }

        var messages = ResponsesParsing.MessagesFromInput(body.Input, body.Instructions);
        if (messages.Count == 0)
        {
            await WriteErrorAsync(http, StatusCodes.Status400BadRequest, "Request 'input' must contain at least one message.").ConfigureAwait(false);
            return;
        }

        var request = new ChannelRequest
        {
            Channel = this.Name,
            Operation = "message.create",
            Input = messages,
            Stream = body.Stream,
            Session = body.PreviousResponseId is null ? null : new ChannelSession { Key = body.PreviousResponseId },
        };

        if (this._options.RunHook is not null)
        {
            var hookContext = new ChannelRunHookContext { Target = context.Host, ProtocolRequest = body };
            request = await this._options.RunHook.OnRequestAsync(request, hookContext, http.RequestAborted).ConfigureAwait(false);
        }

        try
        {
            if (request.Stream)
            {
                await this.WriteStreamAsync(context, request, body.Model, http).ConfigureAwait(false);
            }
            else
            {
                await this.WriteJsonResponseAsync(context, request, body.Model, http).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await WriteErrorAsync(http, StatusCodes.Status500InternalServerError, ex.Message).ConfigureAwait(false);
        }
    }

    private async Task WriteJsonResponseAsync(IChannelContext context, ChannelRequest request, string? model, HttpContext http)
    {
        var result = await context.RunAsync(request, http.RequestAborted).ConfigureAwait(false);
        result = await this.ApplyResponseHookAsync(result, request, http.RequestAborted).ConfigureAwait(false);

        var text = ExtractText(result.ResultObject);
        var response = BuildResponse(NewResponseId(), model, text, result.ResultObject as AgentResponse, status: "completed");

        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(http.Response.Body, response, ResponsesJsonContext.Default.ResponsesResponseModel, http.RequestAborted).ConfigureAwait(false);
    }

    private async Task WriteStreamAsync(IChannelContext context, ChannelRequest request, string? model, HttpContext http)
    {
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";

        var responseId = NewResponseId();
        var itemId = "msg_" + Guid.NewGuid().ToString("N");

        var created = BuildResponse(responseId, model, text: string.Empty, agentResponse: null, status: "in_progress");
        await WriteEventAsync(http, "response.created", new ResponsesStreamResponseEvent { Type = "response.created", Response = created }, ResponsesJsonContext.Default.ResponsesStreamResponseEvent).ConfigureAwait(false);

        var sb = new StringBuilder();
        var updates = ExtractUpdatesAsync(context.StreamAsync(request, http.RequestAborted));
        var transformed = this._options.StreamTransformHook is { } hook
            ? hook.TransformAsync(updates, http.RequestAborted)
            : updates;

        await foreach (var update in transformed.ConfigureAwait(false))
        {
            var delta = update.Text;
            if (!string.IsNullOrEmpty(delta))
            {
                sb.Append(delta);
                var deltaEvent = new ResponsesStreamTextDeltaEvent { ItemId = itemId, OutputIndex = 0, ContentIndex = 0, Delta = delta };
                await WriteEventAsync(http, "response.output_text.delta", deltaEvent, ResponsesJsonContext.Default.ResponsesStreamTextDeltaEvent).ConfigureAwait(false);
            }
        }

        var completed = BuildResponse(responseId, model, sb.ToString(), agentResponse: null, status: "completed", itemId: itemId);
        await WriteEventAsync(http, "response.completed", new ResponsesStreamResponseEvent { Type = "response.completed", Response = completed }, ResponsesJsonContext.Default.ResponsesStreamResponseEvent).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<AgentResponseUpdate> ExtractUpdatesAsync(
        IAsyncEnumerable<HostedStreamItem> items)
    {
        await foreach (var item in items.ConfigureAwait(false))
        {
            if (item is HostedStreamUpdate update)
            {
                yield return update.Update;
            }
        }
    }

    private async ValueTask<HostedRunResult> ApplyResponseHookAsync(HostedRunResult result, ChannelRequest request, CancellationToken cancellationToken)
    {
        if (this._options.ResponseHook is null)
        {
            return result;
        }
        var ctx = new ChannelResponseContext { Request = request, ChannelName = this.Name };
        return await this._options.ResponseHook.OnResponseAsync(result, ctx, cancellationToken).ConfigureAwait(false);
    }

    private static ResponsesResponseModel BuildResponse(string id, string? model, string text, AgentResponse? agentResponse, string status, string? itemId = null)
    {
        var response = new ResponsesResponseModel
        {
            Id = id,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Status = status,
            Model = model,
        };

        if (status == "completed")
        {
            response.Output.Add(new ResponsesOutputMessage
            {
                Id = itemId ?? "msg_" + Guid.NewGuid().ToString("N"),
                Content = { new ResponsesOutputText { Text = text } },
            });

            if (agentResponse?.Usage is { } usage)
            {
                response.Usage = new ResponsesUsageModel
                {
                    InputTokens = (int)(usage.InputTokenCount ?? 0),
                    OutputTokens = (int)(usage.OutputTokenCount ?? 0),
                    TotalTokens = (int)(usage.TotalTokenCount ?? 0),
                };
            }
        }

        return response;
    }

    private static string ExtractText(object? resultObject) => resultObject switch
    {
        AgentResponse response => response.Text,
        AgentResponseUpdate update => update.Text,
        string s => s,
        _ => resultObject?.ToString() ?? string.Empty,
    };

    private static string NewResponseId() => "resp_" + Guid.NewGuid().ToString("N");

    private static async Task WriteEventAsync<T>(HttpContext http, string eventType, T payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(payload, typeInfo);
        var frame = string.Create(CultureInfo.InvariantCulture, $"event: {eventType}\ndata: {json}\n\n");
        await http.Response.WriteAsync(frame, http.RequestAborted).ConfigureAwait(false);
        await http.Response.Body.FlushAsync(http.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteErrorAsync(HttpContext http, int statusCode, string? message)
    {
        http.Response.StatusCode = statusCode;
        http.Response.ContentType = "application/json; charset=utf-8";
        var error = new ResponsesErrorModel { Error = new ResponsesErrorBody { Message = message } };
        await JsonSerializer.SerializeAsync(http.Response.Body, error, ResponsesJsonContext.Default.ResponsesErrorModel, http.RequestAborted).ConfigureAwait(false);
    }
}
