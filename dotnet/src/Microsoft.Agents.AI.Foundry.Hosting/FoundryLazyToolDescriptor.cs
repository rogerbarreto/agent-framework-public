// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Declares a tool that lives in a <em>lazy</em> Foundry toolbox — one whose MCP
/// <c>tools/list</c> handshake is deferred until first invocation.
/// </summary>
/// <remarks>
/// <para>
/// Required when the toolbox contains MCP servers that authenticate as the calling end-user
/// (e.g. Logic Apps connectors via <c>authType: OAuth2</c>): their <c>tools/list</c> cannot
/// succeed under the hosted agent's managed identity at container startup, because the agent
/// MI is a service principal that cannot perform interactive OAuth consent.
/// </para>
/// <para>
/// The framework registers a placeholder <see cref="Microsoft.Extensions.AI.AIFunction"/> per
/// descriptor at startup so the model sees the tool schema and can choose to call it. On first
/// invocation the underlying MCP connection is opened, the request is forwarded with the
/// caller's identity, and any <c>-32007</c> consent-required error is surfaced to the client
/// via an <c>oauth_consent_request</c> Responses-API output item
/// (<see cref="Azure.AI.AgentServer.Responses.Models.OAuthConsentRequestOutputItem"/>). The
/// emitted item carries the consent URL the end-user must visit to complete the OAuth grant.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
public sealed class FoundryLazyToolDescriptor
{
    /// <summary>
    /// Gets or sets the name of the lazy toolbox that hosts this tool. Must also appear in
    /// <see cref="FoundryToolboxOptions.LazyToolboxNames"/>.
    /// </summary>
    public required string ToolboxName { get; set; }

    /// <summary>
    /// Gets or sets the fully-qualified tool name in the Foundry-namespaced form
    /// <c>{server_label}___{tool_name}</c> as returned by the toolbox proxy's <c>tools/list</c>
    /// (e.g. <c>github_oauth___search_issues</c>). Used verbatim as the function name the model
    /// sees and as the MCP <c>tools/call</c> name at invocation time.
    /// </summary>
    public required string ToolName { get; set; }

    /// <summary>
    /// Gets or sets a human-readable description for the tool, surfaced to the model alongside
    /// <see cref="JsonSchema"/> so it can decide when to invoke this tool.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON Schema for the tool's input arguments (the value passed as
    /// <c>function.parameters</c> in the OpenAI function-calling format). At minimum supply
    /// <c>{ "type": "object", "properties": { } }</c>; richer schemas yield better model accuracy.
    /// </summary>
    public JsonElement JsonSchema { get; set; }
}
