// Copyright (c) Microsoft. All rights reserved.

using System;
using ModelContextProtocol;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Shared helpers for surfacing the Foundry Toolbox OAuth-consent JSON-RPC error
/// (<c>-32007</c>) to the response handler so it can emit an <c>oauth_consent_request</c>
/// output item per the published <c>Azure.AI.AgentServer.Responses</c> contract.
/// </summary>
/// <remarks>
/// <para>
/// The Foundry Toolbox MCP gateway surfaces upstream OAuth-consent requirements as a
/// JSON-RPC error with code <c>-32007</c>. The <see cref="System.Exception.Message"/>
/// property carries the consent URL directly (no separate <c>consent_link</c> JSON field).
/// This contract matches the Python reference implementation in
/// <c>agent_framework_foundry_hosting/_responses.py</c> (constant <c>CONSENT_ERROR_CODE = -32007</c>).
/// </para>
/// <para>
/// The error may surface either at MCP connect time (during <c>initialize</c> or
/// <c>tools/list</c>) or during a per-tool <c>tools/call</c>. Both surfaces are handled
/// uniformly through this helper.
/// </para>
/// </remarks>
internal static class FoundryConsentErrorHelper
{
    /// <summary>
    /// JSON-RPC error code returned by the Foundry Toolbox MCP gateway when an upstream tool
    /// source requires interactive OAuth consent from the end user.
    /// </summary>
    /// <remarks>
    /// Production Foundry Toolbox MCP gateways observed in <c>eastus2</c> currently surface the
    /// consent requirement as <c>-32006</c> (with <c>data.grpcStatus = "FailedPrecondition"</c>).
    /// The Python reference implementation in <c>agent_framework_foundry_hosting/_responses.py</c>
    /// uses <c>-32007</c>. Both are accepted by <see cref="IsConsentErrorCode(int)"/> so the
    /// hosting layer remains compatible across gateway versions.
    /// </remarks>
    internal const int ConsentRequiredErrorCode = -32007;

    /// <summary>
    /// Alternate JSON-RPC error code returned by older Foundry Toolbox MCP gateways for the same
    /// OAuth-consent requirement. Observed live on <c>logic-apis-eastus2.consent.azure-apim.net</c>.
    /// </summary>
    internal const int ConsentRequiredErrorCodeAlternate = -32006;

    /// <summary>
    /// Returns <see langword="true"/> when the supplied JSON-RPC error code matches any of the
    /// accepted Foundry Toolbox OAuth-consent codes.
    /// </summary>
    internal static bool IsConsentErrorCode(int errorCode) =>
        errorCode == ConsentRequiredErrorCode || errorCode == ConsentRequiredErrorCodeAlternate;

    /// <summary>
    /// Default <c>server_label</c> applied to emitted <c>oauth_consent_request</c> output items
    /// when no toolbox-specific label is available. Matches the Python reference implementation.
    /// </summary>
    internal const string DefaultServerLabel = "Foundry Toolbox";

    /// <summary>
    /// Determines whether the supplied exception represents the Foundry Toolbox OAuth-consent
    /// requirement (JSON-RPC error <c>-32007</c>).
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <param name="consentLink">When the method returns <see langword="true"/>, contains the
    /// consent URL extracted from <see cref="System.Exception.Message"/>. Otherwise <c>null</c>.</param>
    /// <returns><see langword="true"/> when the exception (or any of its inner exceptions) is an
    /// <see cref="McpProtocolException"/> with <see cref="McpProtocolException.ErrorCode"/> equal
    /// to <see cref="ConsentRequiredErrorCode"/>; <see langword="false"/> otherwise.</returns>
    internal static bool TryGetConsentLink(Exception exception, out string? consentLink)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is McpProtocolException mcp && IsConsentErrorCode((int)mcp.ErrorCode))
            {
                consentLink = mcp.Message;
                return true;
            }
        }

        consentLink = null;
        return false;
    }

    /// <summary>
    /// Records the consent requirement into the ambient <see cref="McpConsentContext.Current"/>
    /// state and cancels its linked <see cref="System.Threading.CancellationTokenSource"/> so the
    /// outer <c>FunctionInvokingChatClient</c> tool loop unwinds. Returns <see langword="true"/>
    /// when the state was populated; <see langword="false"/> when no consent context is registered
    /// for the current request.
    /// </summary>
    internal static bool TryRecord(
        string toolboxName,
        string toolName,
        string consentLink,
        string? serverLabel = null)
    {
        var state = McpConsentContext.Current.Value;
        if (state is null)
        {
            return false;
        }

        var cleanLink = StripMcpErrorPrefix(consentLink);
        var pendingCallId = Microsoft.Extensions.AI.FunctionInvokingChatClient.CurrentContext?.CallContent?.CallId;
        state.Pending = new McpConsentInfo(
            toolboxName,
            toolName,
            cleanLink,
            serverLabel ?? toolboxName,
            pendingCallId);
        state.CancellationSource?.Cancel();
        return true;
    }

    /// <summary>
    /// The ModelContextProtocol SDK prepends <c>"Request failed (remote): "</c> to the
    /// <see cref="System.Exception.Message"/> of <c>McpProtocolException</c> instances surfaced
    /// from JSON-RPC error responses. Strip that prefix so consumers see the raw consent URL.
    /// </summary>
    internal static string StripMcpErrorPrefix(string raw)
    {
        const string Prefix = "Request failed (remote): ";
        return raw.StartsWith(Prefix, StringComparison.Ordinal)
            ? raw.Substring(Prefix.Length)
            : raw;
    }
}
