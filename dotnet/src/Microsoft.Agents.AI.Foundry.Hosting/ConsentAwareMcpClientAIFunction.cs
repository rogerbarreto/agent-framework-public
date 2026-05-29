// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// An <see cref="AIFunction"/> wrapper around <see cref="McpClientTool"/> that intercepts
/// JSON-RPC error <c>-32007</c> (OAuth consent required) from the Foundry Toolboxes proxy and
/// propagates it back to <see cref="AgentFrameworkResponseHandler"/> via
/// <see cref="McpConsentContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// When the proxy returns <c>-32007</c>, the consent URL is stored in <see cref="McpConsentContext.Current"/>
/// and the per-request <see cref="RequestConsentState.CancellationSource"/> is cancelled. This causes
/// <see cref="FunctionInvokingChatClient"/> to stop the tool loop (it guards exceptions with
/// <c>when (!ct.IsCancellationRequested)</c>) and surfaces an <see cref="System.OperationCanceledException"/>
/// to the handler. The handler then emits the <c>oauth_consent_request</c> output item and marks the
/// response as <c>incomplete</c> per the published Responses SDK contract
/// (<c>Azure.AI.AgentServer.Responses.Models.OAuthConsentRequestOutputItem</c>).
/// </para>
/// </remarks>
internal sealed class ConsentAwareMcpClientAIFunction : AIFunction
{
    private readonly McpClientTool _inner;
    private readonly string _toolboxName;

    internal ConsentAwareMcpClientAIFunction(McpClientTool inner, string toolboxName)
    {
        this._inner = inner;
        this._toolboxName = toolboxName;
    }

    internal string ToolboxName => this._toolboxName;

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (FoundryConsentErrorHelper.TryGetConsentLink(ex, out var consentLink))
            {
                FoundryConsentErrorHelper.TryRecord(
                    this._toolboxName,
                    this._inner.Name,
                    consentLink!);

                cancellationToken.ThrowIfCancellationRequested();
            }

            throw;
        }
    }
}
