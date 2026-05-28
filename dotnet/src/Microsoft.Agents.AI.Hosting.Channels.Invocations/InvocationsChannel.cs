// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels.Invocations;

/// <summary>
/// JSON invocation channel. Exposes <c>POST {Path}/invoke</c> for synchronous runs and
/// <c>GET {Path}/{continuationToken}</c> for polling background runs.
/// </summary>
public sealed class InvocationsChannel : Channel
{
    private readonly InvocationsChannelOptions _options;

    /// <summary>Initializes a new instance.</summary>
    public InvocationsChannel(InvocationsChannelOptions options)
    {
        this._options = Throw.IfNull(options);
    }

    /// <inheritdoc />
    public override string Name => "invocations";

    /// <inheritdoc />
    public override string Path => this._options.Path;

    /// <inheritdoc />
    public override ChannelContribution Contribute(IChannelContext context)
    {
        Throw.IfNull(context);
        return new ChannelContribution
        {
            Routes =
            [
                endpoints =>
                {
                    endpoints.MapPost("/invoke", (HttpContext http) => this.HandleInvokeAsync(context, http));
                    endpoints.MapGet("/{continuationToken}", (string continuationToken, HttpContext http) =>
                        this.HandleGetContinuationAsync(context, continuationToken, http));
                },
            ],
        };
    }

    private async Task HandleInvokeAsync(IChannelContext context, HttpContext http)
    {
        InvocationRequestModel? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync(http.Request.Body, InvocationsJsonContext.Default.InvocationRequestModel, http.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            await WriteJsonAsync(http, StatusCodes.Status400BadRequest,
                new InvocationErrorModel { ErrorCode = "invalid_json", Message = ex.Message }, InvocationsJsonContext.Default.InvocationErrorModel).ConfigureAwait(false);
            return;
        }

        if (body?.Input is null)
        {
            await WriteJsonAsync(http, StatusCodes.Status400BadRequest,
                new InvocationErrorModel { ErrorCode = "missing_input", Message = "Request body must include a non-null 'input' property." }, InvocationsJsonContext.Default.InvocationErrorModel).ConfigureAwait(false);
            return;
        }

        var attributes = body.Attributes is null
            ? (IReadOnlyDictionary<string, object?>)System.Collections.Immutable.ImmutableDictionary<string, object?>.Empty
            : body.Attributes;

        var request = new ChannelRequest
        {
            Channel = this.Name,
            Operation = "message.create",
            Input = NormalizeInput(body.Input),
            Attributes = attributes,
            Background = body.Background,
            Session = (body.SessionId is null && body.IsolationKey is null) ? null : new ChannelSession
            {
                Key = body.SessionId,
                IsolationKey = body.IsolationKey,
            },
        };

        if (this._options.RunHook is not null)
        {
            var hookContext = new ChannelRunHookContext { Target = context.Host, ProtocolRequest = body };
            request = await this._options.RunHook.OnRequestAsync(request, hookContext, http.RequestAborted).ConfigureAwait(false);
        }

        if (request.Background)
        {
            var token = await context.Host.RunInBackgroundAsync(request, http.RequestAborted).ConfigureAwait(false);
            await WriteJsonAsync(http, StatusCodes.Status202Accepted,
                new InvocationContinuationModel { Status = StatusFromContinuation(token.Status), ContinuationToken = token.Token },
                InvocationsJsonContext.Default.InvocationContinuationModel).ConfigureAwait(false);
            return;
        }

        try
        {
            var result = await context.RunAsync(request, http.RequestAborted).ConfigureAwait(false);
            await WriteSuccessAsync(http, result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(http, StatusCodes.Status500InternalServerError,
                new InvocationErrorModel { ErrorCode = "run_failed", Message = ex.Message },
                InvocationsJsonContext.Default.InvocationErrorModel).ConfigureAwait(false);
        }
    }

    private async Task HandleGetContinuationAsync(IChannelContext context, string continuationToken, HttpContext http)
    {
        var token = await context.Host.GetContinuationAsync(continuationToken, http.RequestAborted).ConfigureAwait(false);
        if (token is null)
        {
            await WriteJsonAsync(http, StatusCodes.Status404NotFound,
                new InvocationErrorModel { ErrorCode = "unknown_continuation", Message = $"Continuation token '{continuationToken}' is not known." },
                InvocationsJsonContext.Default.InvocationErrorModel).ConfigureAwait(false);
            return;
        }

        switch (token.Status)
        {
            case ContinuationStatus.Queued:
            case ContinuationStatus.Running:
                await WriteJsonAsync(http, StatusCodes.Status202Accepted,
                    new InvocationContinuationModel { Status = StatusFromContinuation(token.Status), ContinuationToken = token.Token },
                    InvocationsJsonContext.Default.InvocationContinuationModel).ConfigureAwait(false);
                break;

            case ContinuationStatus.Completed when token.Result is not null:
                await WriteSuccessAsync(http, token.Result).ConfigureAwait(false);
                break;

            case ContinuationStatus.Failed:
                await WriteJsonAsync(http, StatusCodes.Status500InternalServerError,
                    new InvocationErrorModel { ErrorCode = "run_failed", Message = token.Error },
                    InvocationsJsonContext.Default.InvocationErrorModel).ConfigureAwait(false);
                break;

            default:
                await WriteJsonAsync(http, StatusCodes.Status500InternalServerError,
                    new InvocationErrorModel { ErrorCode = "unknown_state", Message = $"Continuation in unexpected state '{token.Status}'." },
                    InvocationsJsonContext.Default.InvocationErrorModel).ConfigureAwait(false);
                break;
        }
    }

    private static async Task WriteSuccessAsync(HttpContext http, HostedRunResult result)
    {
        var text = result.ResultObject switch
        {
            AgentResponse response => response.Text,
            AgentResponseUpdate update => update.Text,
            string s => s,
            _ => result.ResultObject?.ToString(),
        };

        var model = new InvocationResponseModel
        {
            Status = "completed",
            Text = text,
            SessionId = result.Session?.Key,
        };

        await WriteJsonAsync(http, StatusCodes.Status200OK, model, InvocationsJsonContext.Default.InvocationResponseModel).ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync<T>(HttpContext http, int statusCode, T payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        http.Response.StatusCode = statusCode;
        http.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(http.Response.Body, payload, typeInfo, http.RequestAborted).ConfigureAwait(false);
    }

    private static string StatusFromContinuation(ContinuationStatus status) => status switch
    {
        ContinuationStatus.Queued => "queued",
        ContinuationStatus.Running => "running",
        ContinuationStatus.Completed => "completed",
        ContinuationStatus.Failed => "failed",
        _ => "unknown",
    };

    private static string NormalizeInput(object input)
    {
        return input switch
        {
            JsonElement el when el.ValueKind == JsonValueKind.String => el.GetString() ?? string.Empty,
            JsonElement el => el.ToString(),
            string s => s,
            _ => input.ToString() ?? string.Empty,
        };
    }
}