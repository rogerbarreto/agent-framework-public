// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// An <see cref="AIFunction"/> wrapper around <see cref="McpClientTool"/> that intercepts
/// JSON-RPC error -32006 (OAuth consent required) from the Foundry Toolsets proxy and
/// propagates it back to <see cref="AgentFrameworkResponseHandler"/> via
/// <see cref="McpConsentContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// When the proxy returns -32006, the consent URL is stored in <see cref="McpConsentContext.Current"/>
/// and the per-request <see cref="RequestConsentState.CancellationSource"/> is cancelled. This causes
/// <see cref="FunctionInvokingChatClient"/> to stop the tool loop (it guards
/// exceptions with <c>when (!ct.IsCancellationRequested)</c>) and surfaces an
/// <see cref="OperationCanceledException"/> to the handler. The handler then emits the
/// <c>mcp_approval_request</c> output item and marks the response as <c>incomplete</c>.
/// </para>
/// </remarks>
internal sealed class ConsentAwareMcpClientAIFunction : AIFunction
{
    private readonly McpClientTool _inner;
    private readonly string _toolboxName;
    private readonly OAuthConsentLinkPolicy _consentLinkPolicy;

    internal ConsentAwareMcpClientAIFunction(
        McpClientTool inner,
        string toolboxName,
        OAuthConsentLinkPolicy consentLinkPolicy)
    {
        this._inner = inner;
        this._toolboxName = toolboxName;
        this._consentLinkPolicy = consentLinkPolicy;
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
        catch (McpProtocolException ex) when ((int)ex.ErrorCode == -32006)
        {
            if (!this._consentLinkPolicy.IsAllowed(ex.Message))
            {
                throw new InvalidOperationException(
                    "The OAuth consent response did not match the configured allowed origins.");
            }

            var state = McpConsentContext.Current.Value;
            if (state is not null)
            {
                state.Pending = new McpConsentInfo(this._toolboxName, this._inner.Name, ex.Message);
                state.CancellationSource?.Cancel();
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw; // fallback if the CT wasn't cancelled for some reason
        }
    }
}
