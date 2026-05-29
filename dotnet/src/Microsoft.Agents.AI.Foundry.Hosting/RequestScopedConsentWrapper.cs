// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Per-request <see cref="AIFunction"/> wrapper that captures the request-scoped
/// <see cref="RequestConsentState"/> by closure and records OAuth-consent errors
/// without relying on <see cref="System.Threading.AsyncLocal{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Production runtimes (e.g., the Foundry hosted-agent container's Responses
/// orchestrator) do not always propagate <see cref="System.Threading.ExecutionContext"/>
/// through to the function-invocation boundary, which means
/// <see cref="McpConsentContext.Current"/> can be <see langword="null"/> when the
/// underlying MCP function throws. The handler wraps every tool with this type at
/// request time so the consent state is reachable via a direct closure reference.
/// </para>
/// </remarks>
internal sealed class RequestScopedConsentWrapper : AIFunction
{
    private readonly AIFunction _inner;
    private readonly RequestConsentState _state;
    private readonly string _toolboxName;

    internal RequestScopedConsentWrapper(AIFunction inner, RequestConsentState state, string toolboxName)
    {
        this._inner = inner;
        this._state = state;
        this._toolboxName = toolboxName;
    }

    public override string Name => this._inner.Name;

    public override string Description => this._inner.Description;

    public override JsonElement JsonSchema => this._inner.JsonSchema;

    public override JsonElement? ReturnJsonSchema => this._inner.ReturnJsonSchema;

    public override JsonSerializerOptions JsonSerializerOptions => this._inner.JsonSerializerOptions;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await this._inner.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException &&
            FoundryConsentErrorHelper.TryGetConsentLink(ex, out var consentLink))
        {
            // Capture the call_id of the model-issued function call that just failed so the
            // handler can emit a synthetic function_call_output paired with the consent item.
            // The orchestrator otherwise rejects subsequent previous_response_id retries with
            // "No tool output found for function call ...".
            var pendingCallId = Microsoft.Extensions.AI.FunctionInvokingChatClient.CurrentContext?.CallContent?.CallId;

            this._state.Pending = new McpConsentInfo(
                this._toolboxName,
                this._inner.Name,
                FoundryConsentErrorHelper.StripMcpErrorPrefix(consentLink!),
                this._toolboxName,
                pendingCallId);
            this._state.CancellationSource?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }
}
