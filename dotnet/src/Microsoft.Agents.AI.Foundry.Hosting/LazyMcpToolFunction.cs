// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Placeholder <see cref="AIFunction"/> for a tool that lives in a lazy Foundry toolbox.
/// Exposes a caller-supplied schema at startup; opens the MCP connection on first invocation
/// and delegates to <see cref="ConsentAwareMcpClientAIFunction"/> so the
/// <c>-32007 CONSENT_REQUIRED</c> → <c>oauth_consent_request</c> propagation continues to work.
/// </summary>
/// <remarks>
/// Required because Foundry toolbox proxies aggregate per-source errors at <c>tools/list</c>
/// (returning <c>-32007</c> with zero tools when any source needs OAuth consent), making
/// eager startup discovery impossible for OAuth-user-identity toolboxes. Splitting those into
/// their own toolbox and registering them via <see cref="FoundryToolboxOptions.LazyToolboxNames"/>
/// lets the rest of the agent's tools load eagerly while OAuth tools remain available to the
/// model with their schemas pre-declared. The same <c>-32007</c> may surface either during the
/// initial MCP connect (handled here) or during the subsequent <c>tools/call</c> (handled by
/// <see cref="ConsentAwareMcpClientAIFunction"/>); both paths route through the shared
/// <see cref="FoundryConsentErrorHelper"/> so the response handler sees a uniform shape.
/// </remarks>
internal sealed class LazyMcpToolFunction : AIFunction
{
    private readonly FoundryToolboxService _owner;
    private readonly FoundryLazyToolDescriptor _descriptor;
    private ConsentAwareMcpClientAIFunction? _resolved;

    internal LazyMcpToolFunction(FoundryToolboxService owner, FoundryLazyToolDescriptor descriptor)
    {
        this._owner = owner;
        this._descriptor = descriptor;
    }

    internal string ToolboxName => this._descriptor.ToolboxName;

    public override string Name => this._descriptor.ToolName;

    public override string Description => this._descriptor.Description;

    public override JsonElement JsonSchema => this._descriptor.JsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var resolved = this._resolved;
        if (resolved is null)
        {
            McpClient client;
            try
            {
                client = await this._owner.EnsureLazyToolboxOpenAsync(
                    this._descriptor.ToolboxName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException &&
                FoundryConsentErrorHelper.TryGetConsentLink(ex, out var consentLink))
            {
                // Connect-time consent surface: the Foundry Toolbox gateway returned -32007
                // during MCP initialize or tools/list before any tool call could be made.
                // Record on the per-request consent state so the handler emits an
                // oauth_consent_request output item, then cancel the tool loop.
                FoundryConsentErrorHelper.TryRecord(
                    this._descriptor.ToolboxName,
                    this._descriptor.ToolName,
                    consentLink!);

                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }

            var protocolTool = new Tool
            {
                Name = this._descriptor.ToolName,
                Description = this._descriptor.Description,
                InputSchema = this._descriptor.JsonSchema,
            };

            var mcpTool = new McpClientTool(client, protocolTool);
            var candidate = new ConsentAwareMcpClientAIFunction(mcpTool, this._descriptor.ToolboxName);

            // First writer wins; subsequent concurrent first-invocations reuse the winner.
            resolved = Interlocked.CompareExchange(ref this._resolved, candidate, null) ?? candidate;
        }

        return await resolved.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
    }
}
