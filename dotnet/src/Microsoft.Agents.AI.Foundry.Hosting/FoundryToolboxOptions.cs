// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Options for Foundry Toolbox MCP integration.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
public sealed class FoundryToolboxOptions
{
    /// <summary>
    /// Gets the list of toolbox names to connect to at startup.
    /// Each name corresponds to a toolbox registered in the Foundry project.
    /// The platform proxy URL is constructed as:
    /// <c>{FOUNDRY_PROJECT_ENDPOINT}/toolboxes/{toolboxName}/mcp?api-version={ApiVersion}</c>
    /// per <c>tools-integration-spec.md</c> §2–§3.
    /// </summary>
    public IList<string> ToolboxNames { get; } = [];

    /// <summary>
    /// Gets the list of toolbox names that are <em>registered but not opened</em> at startup.
    /// Use this for toolboxes whose MCP servers authenticate as the calling end-user
    /// (<c>authType: OAuth2</c> connections, e.g. Logic Apps), since their <c>tools/list</c>
    /// cannot succeed under the hosted agent's managed identity. The MCP client is opened on
    /// first invocation and any <c>-32007</c> consent-required error is surfaced through the
    /// <c>oauth_consent_request</c> Responses-API output item
    /// (<see cref="Azure.AI.AgentServer.Responses.Models.OAuthConsentRequestOutputItem"/>).
    /// Each name listed here must have at least one
    /// matching entry in <see cref="LazyToolDescriptors"/>.
    /// </summary>
    public IList<string> LazyToolboxNames { get; } = [];

    /// <summary>
    /// Gets the tool schemas for lazy toolboxes (see <see cref="LazyToolboxNames"/>). Each
    /// descriptor becomes a placeholder <see cref="Microsoft.Extensions.AI.AIFunction"/> the
    /// model can call; the framework opens the underlying MCP connection on first use and
    /// forwards the call.
    /// </summary>
    public IList<FoundryLazyToolDescriptor> LazyToolDescriptors { get; } = [];

    /// <summary>
    /// Gets or sets the Toolboxes API version to use when constructing proxy URLs.
    /// </summary>
    public string ApiVersion { get; set; } = "v1";

    /// <summary>
    /// Gets or sets a value indicating whether per-request toolbox markers (referenced via
    /// <c>foundry-toolbox://</c> on the wire) are restricted to toolboxes pre-registered
    /// via <see cref="ToolboxNames"/>. When <see langword="true"/> (the default), a request
    /// that references an unknown toolbox is rejected. When <see langword="false"/>, the
    /// server lazily opens an MCP connection for the referenced toolbox on first use and
    /// caches it.
    /// </summary>
    public bool StrictMode { get; set; } = true;

    /// <summary>
    /// For testing only: overrides the toolbox proxy base URL (skipping the
    /// <c>FOUNDRY_PROJECT_ENDPOINT</c>-derived default). When set, the proxy URL
    /// becomes <c>{EndpointOverride}/toolboxes/{toolboxName}/mcp?api-version={ApiVersion}</c>.
    /// Not part of the public API.
    /// </summary>
    internal string? EndpointOverride { get; set; }
}
