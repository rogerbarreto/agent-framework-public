# Hosted-Toolbox

A hosted Foundry agent that loads tools from a Foundry Toolbox via the AF Foundry hosting bridge.

The agent declares one `FoundryAITool.CreateHostedMcpToolbox(name)` marker; `AddFoundryToolboxes(name)` registers a `FoundryToolboxService` that resolves the marker into the individual MCP tools the toolbox bundles, connecting to the Foundry Toolsets MCP proxy at startup and discovering tools via `tools/list`.

## Prerequisites

- A Microsoft Foundry project with a Toolbox configured.
- Azure CLI logged in (`az login`).
- Set environment variables:
  - `AZURE_AI_PROJECT_ENDPOINT`
  - `AZURE_AI_MODEL_DEPLOYMENT_NAME` (default `gpt-4o`)
  - `FOUNDRY_TOOLBOX_NAME` (default `my-toolset`)
  - `FOUNDRY_AGENT_TOOLSET_ENDPOINT` — auto-injected in hosted containers; for local `dotnet run` set to `{AZURE_AI_PROJECT_ENDPOINT}/toolboxes`.

## Run

```powershell
dotnet run --tl:off
```

## Related samples

- [`Hosted-Toolbox-AuthPaths/`](../Hosted-Toolbox-AuthPaths/) — extends this pattern with a five-tool toolbox demonstrating different MCP-tool authentication paths (key, Entra MI, custom OAuth, inline `Authorization`) and a REPL client that handles the OAuth consent loop.
- [`Hosted-McpTools/`](../Hosted-McpTools/) — contrasts client-side `McpClient` vs server-side `HostedMcpServerTool` for non-toolbox MCP servers.
