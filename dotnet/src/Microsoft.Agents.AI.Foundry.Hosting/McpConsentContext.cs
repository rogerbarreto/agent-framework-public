// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Carries OAuth consent information surfaced by the Foundry Toolbox MCP gateway when an upstream
/// tool source requires the end-user to complete an OAuth consent grant. Produced by the MCP
/// connect / tool-invoke paths (see <see cref="ConsentAwareMcpClientAIFunction"/> and
/// <see cref="LazyMcpToolFunction"/>) and consumed by <see cref="AgentFrameworkResponseHandler"/>
/// to emit an <c>oauth_consent_request</c> output item per the published Responses SDK contract.
/// </summary>
/// <param name="ToolboxName">The toolbox name that owns the tool source.</param>
/// <param name="ToolName">Fully-qualified tool name (e.g., <c>github_oauth___get_me</c>). May be
/// empty when consent fires during connect / <c>tools/list</c> rather than a specific tool call.</param>
/// <param name="ConsentLink">The OAuth consent URL the user must visit to grant access.</param>
/// <param name="ServerLabel">Logical label used as the <c>server_label</c> field on the emitted
/// output item. Defaults to the toolbox name.</param>
/// <param name="CallId">The OpenAI Responses <c>call_id</c> of the function-call that triggered
/// the consent error, when consent is surfaced from a <c>tools/call</c> path. <see langword="null"/>
/// or empty when consent fires at MCP connect / <c>tools/list</c> time (no model-issued call yet).
/// When present, the response handler emits a synthetic <c>function_call_output</c> item before the
/// <c>oauth_consent_request</c> item to satisfy the orchestrator's pairing requirement on
/// subsequent <c>previous_response_id</c>-based retries.</param>
internal sealed record McpConsentInfo(string ToolboxName, string ToolName, string ConsentLink, string ServerLabel, string? CallId = null);

/// <summary>
/// Per-request mutable state shared between <see cref="ConsentAwareMcpClientAIFunction"/> (child context)
/// and <see cref="AgentFrameworkResponseHandler"/> (parent context) via <see cref="McpConsentContext.Current"/>.
/// </summary>
/// <remarks>
/// Because <see cref="AsyncLocal{T}"/> only flows values DOWN from parent to children,
/// we use a shared reference type so children can mutate it and the parent observes the mutations.
/// </remarks>
internal sealed class RequestConsentState
{
    /// <summary>Consent information set by the consent-aware components when JSON-RPC error
    /// <c>-32007</c> (OAuth consent required) is surfaced by the Foundry Toolbox gateway.</summary>
    internal McpConsentInfo? Pending { get; set; }

    /// <summary>The linked CTS to cancel when consent is required.</summary>
    internal CancellationTokenSource? CancellationSource { get; set; }
}

/// <summary>
/// Async-local context that enables <see cref="ConsentAwareMcpClientAIFunction"/>
/// to signal a consent error back to <see cref="AgentFrameworkResponseHandler"/> through the
/// <see cref="FunctionInvokingChatClient"/> tool loop. Flows with the async ExecutionContext.
/// </summary>
internal static class McpConsentContext
{
    /// <summary>
    /// Holds the shared <see cref="RequestConsentState"/> for the current request.
    /// Set once by the handler; read and mutated by the tool wrapper.
    /// </summary>
    internal static readonly AsyncLocal<RequestConsentState?> Current = new();
}
