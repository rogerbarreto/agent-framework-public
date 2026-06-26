// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
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
    public override ChannelContribution Contribute(ChannelContext context)
    {
        Throw.IfNull(context);
        return new ChannelContribution
        {
            Routes = [endpoints => endpoints.MapPost("/", (HttpContext http) => this.HandleAsync(context, http))],
        };
    }

    private async Task HandleAsync(ChannelContext context, HttpContext http)
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

        IReadOnlyList<ChatMessage> messages;
        try
        {
            messages = ResponsesParsing.MessagesFromInput(body.Input);
        }
        catch (FormatException ex)
        {
            await WriteErrorAsync(http, StatusCodes.Status422UnprocessableEntity, ex.Message).ConfigureAwait(false);
            return;
        }

        // Session anchoring (mirrors Python `_handle`): an explicit `previous_response_id` wins; else a
        // host-lifted Foundry chat isolation key; else the freshly minted response id anchors the first turn.
        var previousResponseId = string.IsNullOrEmpty(body.PreviousResponseId) ? null : body.PreviousResponseId;
        ChannelSession? session = previousResponseId is not null ? new ChannelSession { IsolationKey = previousResponseId } : null;

        if (session is null)
        {
            var chatKey = IsolationKeys.Current?.ChatKey;
            if (!string.IsNullOrEmpty(chatKey))
            {
                session = new ChannelSession { IsolationKey = chatKey };
            }
        }

        var responseId = (this._options.ResponseIdFactory ?? s_defaultResponseIdFactory)(previousResponseId);
        session ??= new ChannelSession { IsolationKey = responseId };

        // The minted response id (and any previous id) travel on attributes so a host-side history provider
        // can anchor the storage chain on the same handle the envelope reports.
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { ["response_id"] = responseId };
        if (previousResponseId is not null)
        {
            attributes["previous_response_id"] = previousResponseId;
        }

        var request = new ChannelRequest(this.Name, "message.create", messages)
        {
            Stream = body.Stream,
            Session = session,
            Identity = ResponsesParsing.ParseIdentity(body, this.Name),
            Options = ResponsesParsing.BuildOptions(body),
            Attributes = attributes,
        };

        if (this._options.RunHook is not null)
        {
            var hookContext = new ChannelRunHookContext(context.Host) { ProtocolRequest = body };
            request = await this._options.RunHook.OnRequestAsync(request, hookContext, http.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            // Default behavior strips parsed generation options so untrusted callers cannot inject parameters.
            request.Options = null;
        }

        try
        {
            if (request.Stream)
            {
                await this.WriteStreamAsync(context, request, body.Model, responseId, http).ConfigureAwait(false);
            }
            else
            {
                await this.WriteJsonResponseAsync(context, request, body.Model, responseId, http).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await WriteErrorAsync(http, StatusCodes.Status500InternalServerError, ex.Message).ConfigureAwait(false);
        }
    }

    private async Task WriteJsonResponseAsync(ChannelContext context, ChannelRequest request, string? model, string responseId, HttpContext http)
    {
        var result = await context.RunAsync(request, http.RequestAborted).ConfigureAwait(false);
        result = await this.ApplyResponseHookAsync(result, request, http.RequestAborted).ConfigureAwait(false);

        var response = BuildEnvelope(responseId, model, status: "completed");
        response.Output.AddRange(BuildOutputItems(result.ResultObject));
        response.Usage = BuildUsage(result.ResultObject as AgentResponse);

        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(http.Response.Body, response, ResponsesJsonContext.Default.ResponsesResponseModel, http.RequestAborted).ConfigureAwait(false);
    }

    private async Task WriteStreamAsync(ChannelContext context, ChannelRequest request, string? model, string responseId, HttpContext http)
    {
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";

        var itemId = "msg_" + Guid.NewGuid().ToString("N");
        var sb = new StringBuilder();
        HostedRunResult? finalResult = null;

        try
        {
            var created = BuildEnvelope(responseId, model, status: "in_progress");
            await WriteEventAsync(http, "response.created", new ResponsesStreamResponseEvent { Type = "response.created", Response = created }, ResponsesJsonContext.Default.ResponsesStreamResponseEvent).ConfigureAwait(false);

            var addedItem = new ResponsesOutputItem { Type = "message", Id = itemId, Role = "assistant", Status = "in_progress", Content = [] };
            await WriteEventAsync(http, "response.output_item.added", new ResponsesStreamOutputItemEvent { Type = "response.output_item.added", OutputIndex = 0, Item = addedItem }, ResponsesJsonContext.Default.ResponsesStreamOutputItemEvent).ConfigureAwait(false);
            await WriteEventAsync(http, "response.content_part.added", new ResponsesStreamContentPartEvent { Type = "response.content_part.added", ItemId = itemId, OutputIndex = 0, ContentIndex = 0, Part = new ResponsesOutputText() }, ResponsesJsonContext.Default.ResponsesStreamContentPartEvent).ConfigureAwait(false);

            var updates = StreamUpdatesAsync(context.StreamAsync(request, http.RequestAborted), r => finalResult = r);
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
        }
        catch (Exception ex)
        {
            // Once the SSE stream has started, surface the error as a Responses `response.failed` event
            // rather than an (invalid) post-headers JSON error.
            var failed = BuildEnvelope(responseId, model, status: "failed");
            failed.Error = new ResponsesErrorBody { Message = ex.Message };
            await WriteEventAsync(http, "response.failed", new ResponsesStreamResponseEvent { Type = "response.failed", Response = failed }, ResponsesJsonContext.Default.ResponsesStreamResponseEvent).ConfigureAwait(false);
            return;
        }

        // Apply the response hook to the final streamed result (mirrors Python's stream get_final_response).
        if (finalResult is not null)
        {
            finalResult = await this.ApplyResponseHookAsync(finalResult, request, http.RequestAborted).ConfigureAwait(false);
        }

        var finalText = sb.ToString();
        await WriteEventAsync(http, "response.output_text.done", new ResponsesStreamTextDoneEvent { ItemId = itemId, OutputIndex = 0, ContentIndex = 0, Text = finalText }, ResponsesJsonContext.Default.ResponsesStreamTextDoneEvent).ConfigureAwait(false);

        var donePart = new ResponsesOutputText { Text = finalText };
        await WriteEventAsync(http, "response.content_part.done", new ResponsesStreamContentPartEvent { Type = "response.content_part.done", ItemId = itemId, OutputIndex = 0, ContentIndex = 0, Part = donePart }, ResponsesJsonContext.Default.ResponsesStreamContentPartEvent).ConfigureAwait(false);

        var doneItem = new ResponsesOutputItem { Type = "message", Id = itemId, Role = "assistant", Status = "completed", Content = [donePart] };
        await WriteEventAsync(http, "response.output_item.done", new ResponsesStreamOutputItemEvent { Type = "response.output_item.done", OutputIndex = 0, Item = doneItem }, ResponsesJsonContext.Default.ResponsesStreamOutputItemEvent).ConfigureAwait(false);

        var completed = BuildEnvelope(responseId, model, status: "completed");
        if (finalResult?.ResultObject is AgentResponse hookResponse)
        {
            completed.Output.AddRange(BuildOutputItems(hookResponse));
        }
        else
        {
            completed.Output.Add(doneItem);
        }
        await WriteEventAsync(http, "response.completed", new ResponsesStreamResponseEvent { Type = "response.completed", Response = completed }, ResponsesJsonContext.Default.ResponsesStreamResponseEvent).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<AgentResponseUpdate> StreamUpdatesAsync(
        IAsyncEnumerable<HostedStreamItem> items,
        Action<HostedRunResult> captureFinal)
    {
        await foreach (var item in items.ConfigureAwait(false))
        {
            switch (item)
            {
                case HostedStreamUpdate update:
                    yield return update.Update;
                    break;
                case HostedStreamCompleted completed:
                    captureFinal(completed.Result);
                    break;
            }
        }
    }

    private async ValueTask<HostedRunResult> ApplyResponseHookAsync(HostedRunResult result, ChannelRequest request, CancellationToken cancellationToken)
    {
        if (this._options.ResponseHook is null)
        {
            return result;
        }
        var ctx = new ChannelResponseContext(request, this.Name);
        return await this._options.ResponseHook.OnResponseAsync(result, ctx, cancellationToken).ConfigureAwait(false);
    }

    private static ResponsesResponseModel BuildEnvelope(string id, string? model, string status) => new()
    {
        Id = id,
        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        Status = status,
        Model = string.IsNullOrEmpty(model) ? "agent" : model,
    };

    private static ResponsesUsageModel? BuildUsage(AgentResponse? agentResponse)
    {
        if (agentResponse?.Usage is not { } usage)
        {
            return null;
        }

        return new ResponsesUsageModel
        {
            InputTokens = (int)(usage.InputTokenCount ?? 0),
            OutputTokens = (int)(usage.OutputTokenCount ?? 0),
            TotalTokens = (int)(usage.TotalTokenCount ?? 0),
        };
    }

    /// <summary>
    /// Render an agent result's messages as Responses output items, mirroring the Python channel: consecutive
    /// text coalesces into one assistant message item; reasoning, function calls, and function results each
    /// become their own typed output item. Unmodeled content falls back to text.
    /// </summary>
    private static List<ResponsesOutputItem> BuildOutputItems(object? resultObject)
    {
        var items = new List<ResponsesOutputItem>();
        List<ResponsesOutputText>? messageText = null;

        void Flush()
        {
            if (messageText is { Count: > 0 })
            {
                items.Add(new ResponsesOutputItem
                {
                    Type = "message",
                    Id = "msg_" + Guid.NewGuid().ToString("N"),
                    Role = "assistant",
                    Status = "completed",
                    Content = messageText,
                });
                messageText = null;
            }
        }

        foreach (var message in ExtractMessages(resultObject))
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent reasoning:
                        Flush();
                        items.Add(new ResponsesOutputItem
                        {
                            Type = "reasoning",
                            Id = "rs_" + Guid.NewGuid().ToString("N"),
                            Summary = [new ResponsesReasoningSummary { Text = reasoning.Text }],
                        });
                        break;
                    case FunctionCallContent call:
                        Flush();
                        items.Add(new ResponsesOutputItem
                        {
                            Type = "function_call",
                            Id = "fc_" + Guid.NewGuid().ToString("N"),
                            CallId = call.CallId,
                            Name = call.Name,
                            Arguments = SerializeArguments(call.Arguments),
                            Status = "completed",
                        });
                        break;
                    case FunctionResultContent functionResult:
                        Flush();
                        items.Add(new ResponsesOutputItem
                        {
                            Type = "function_call_output",
                            Id = "fco_" + Guid.NewGuid().ToString("N"),
                            CallId = functionResult.CallId,
                            Output = functionResult.Result?.ToString() ?? string.Empty,
                            Status = "completed",
                        });
                        break;
                    case TextContent text:
                        (messageText ??= []).Add(new ResponsesOutputText { Text = text.Text });
                        break;
                    default:
                        var fallback = content.ToString();
                        if (!string.IsNullOrEmpty(fallback))
                        {
                            (messageText ??= []).Add(new ResponsesOutputText { Text = fallback });
                        }
                        break;
                }
            }
        }

        Flush();

        if (items.Count == 0)
        {
            items.Add(new ResponsesOutputItem
            {
                Type = "message",
                Id = "msg_" + Guid.NewGuid().ToString("N"),
                Role = "assistant",
                Status = "completed",
                Content = [new ResponsesOutputText { Text = ExtractText(resultObject) }],
            });
        }

        return items;
    }

    private static IEnumerable<ChatMessage> ExtractMessages(object? resultObject) => resultObject switch
    {
        AgentResponse response => response.Messages,
        WorkflowRunResult workflow => FlattenWorkflowOutputs(workflow.Outputs),
        _ => [],
    };

    private static IEnumerable<ChatMessage> FlattenWorkflowOutputs(IReadOnlyList<object?> outputs)
    {
        foreach (var output in outputs)
        {
            switch (output)
            {
                case null:
                    break;
                case ChatMessage message:
                    yield return message;
                    break;
                case AgentResponse response:
                    foreach (var message in response.Messages)
                    {
                        yield return message;
                    }

                    break;
                case IEnumerable<ChatMessage> messages:
                    foreach (var message in messages)
                    {
                        yield return message;
                    }

                    break;
                case string text:
                    yield return new ChatMessage(ChatRole.Assistant, text);
                    break;
                default:
                    yield return new ChatMessage(ChatRole.Assistant, output.ToString() ?? string.Empty);
                    break;
            }
        }
    }

    private static string ExtractText(object? resultObject) => resultObject switch
    {
        AgentResponse response => response.Text,
        AgentResponseUpdate update => update.Text,
        string s => s,
        _ => resultObject?.ToString() ?? string.Empty,
    };

    private static string SerializeArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return "{}";
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var argument in arguments)
            {
                writer.WritePropertyName(argument.Key);
                WriteArgumentValue(writer, argument.Value);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteArgumentValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case JsonElement element: element.WriteTo(writer); break;
            case string s: writer.WriteStringValue(s); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case int i: writer.WriteNumberValue(i); break;
            case long l: writer.WriteNumberValue(l); break;
            case double d: writer.WriteNumberValue(d); break;
            case float f: writer.WriteNumberValue(f); break;
            case decimal m: writer.WriteNumberValue(m); break;
            default: writer.WriteStringValue(value.ToString()); break;
        }
    }

    private static readonly Func<string?, string> s_defaultResponseIdFactory = _ => "resp_" + Guid.NewGuid().ToString("N");

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
