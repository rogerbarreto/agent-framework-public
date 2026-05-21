# Hosted-Toolbox

A hosted Foundry agent that loads tools from a Foundry Toolbox via the AF Foundry hosting bridge.

The agent declares one `FoundryAITool.CreateHostedMcpToolbox(name)` marker; `AddFoundryToolboxes(name)` registers a `FoundryToolboxService` that resolves the marker into the individual MCP tools the toolbox bundles, connecting to the Foundry Toolsets MCP proxy at startup and discovering tools via `tools/list`.

## Prerequisites

- A Microsoft Foundry project with a Toolbox configured.
- Azure CLI logged in (`az login`).
- Set environment variables:
  - `AZURE_AI_PROJECT_ENDPOINT` (local-dev) or `FOUNDRY_PROJECT_ENDPOINT` (auto-injected in hosted containers)
  - `AZURE_AI_MODEL_DEPLOYMENT_NAME` (default `gpt-4o`)
  - `FOUNDRY_TOOLBOX_NAME` (default `my-toolset`)

The `Foundry.Hosting` package builds the toolbox proxy URL from `FOUNDRY_PROJECT_ENDPOINT` as `{FOUNDRY_PROJECT_ENDPOINT}/toolboxes/{FOUNDRY_TOOLBOX_NAME}/mcp?api-version=v1` per [`tools-integration-spec.md`](https://github.com/microsoft/AgentSchema/blob/main/specs/agents/hosted_agents/container-spec/docs/tools-integration-spec.md) §2–§3.

## Run

```powershell
dotnet run --tl:off
```

## Related samples

- [`Hosted-Toolbox-AuthPaths/`](../Hosted-Toolbox-AuthPaths/) — extends this pattern with a five-tool toolbox demonstrating different MCP-tool authentication paths (key, Entra MI, custom OAuth, inline `Authorization`) and a REPL client that handles the OAuth consent loop.
- [`Hosted-McpTools/`](../Hosted-McpTools/) — contrasts client-side `McpClient` vs server-side `HostedMcpServerTool` for non-toolbox MCP servers.
