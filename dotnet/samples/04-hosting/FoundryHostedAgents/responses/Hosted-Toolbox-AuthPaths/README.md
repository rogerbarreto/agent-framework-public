# Hosted Toolbox — Authentication Paths

A hosted Foundry agent backed by a single Foundry Toolbox that bundles MCP tools using **five different authentication paths**. The educational surface lives in the toolbox configuration (which you provision in the Foundry portal) and in this README — the agent code itself is identical to the existing [`Hosted-Toolbox/`](../Hosted-Toolbox/) sample.

The companion REPL client at [`Using-Samples/Hosted-Toolbox-AuthPaths-Client/`](../Using-Samples/Hosted-Toolbox-AuthPaths-Client/) drives the agent interactively and handles the OAuth `mcp_approval_request` consent loop emitted when the OAuth-managed tool needs user consent.

## What this sample teaches

| Aspect | This sample | Existing siblings |
|---|---|---|
| Toolbox marker pattern | `FoundryAITool.CreateHostedMcpToolbox(name)` + `AddFoundryToolboxes(name)` | Same as [`Hosted-Toolbox/`](../Hosted-Toolbox/) |
| Tools per toolbox | **Five MCP tools, each with a different auth method** | `Hosted-Toolbox/`: typically one demo tool |
| Consumption | Server-side (Foundry resolves the marker) | Same |
| Client | Custom REPL with **OAuth approval loop** | `Hosted-Toolbox/`: any client; OAuth not handled |

Related samples:
- [`Hosted-Toolbox/`](../Hosted-Toolbox/) — simpler single-tool toolbox.
- [`Hosted-McpTools/`](../Hosted-McpTools/) — contrasts client-side `McpClient` vs server-side `HostedMcpServerTool` for non-toolbox MCP servers.

## Authentication-path matrix

| # | Auth method | MCP target | Portal connection category | What flows where |
|---|---|---|---|---|
| 1 | **Key-based via project connection** | GitHub MCP at `https://api.githubcopilot.com/mcp` | **Custom Keys** | A PAT stored as `Authorization: Bearer <pat>` lives in the Foundry connection. The toolbox proxy reads it server-side and injects on every MCP call. |
| 2 | **Microsoft Entra — agent identity** | Self-hosted Azure MCP Server (ACA URL — see prereq below) | **Microsoft Entra ID — Agent Identity** | The Foundry Agent Service requests an Entra token using the agent's managed identity and passes it to the MCP server. |
| 3 | **Microsoft Entra — project managed identity** | Same Azure MCP Server URL as #2 (different connection) | **Microsoft Entra ID — Project Managed Identity** | Same as #2 but the principal is the project MI rather than the agent identity. |
| 4 | **Custom OAuth (identity passthrough)** | Outlook Mail Agent 365 server (catalog discovery) | **OAuth Identity Passthrough — Custom OAuth** | The end user signs in to the OAuth app you registered. Their token (scoped to `McpServers.Mail.All`) is used per request. First request emits an `mcp_approval_request` with the consent URL. |
| 5 | **Inline `Authorization` (anti-pattern)** | `https://gitmcp.io/Azure/azure-rest-api-specs` | **None** — auth is set on the tool entry itself via the `authorization` field | A literal bearer string is hardcoded in the toolbox tool definition. **Do not do this in production** — there's no rotation, no secret store, no per-user identity. Shown for completeness. |

> **Pre-publish caveat for paths #2 and #3**: Before you publish your agent, all agents in a Foundry project share the **same project managed identity** as their agent identity. So at runtime the two connections will behave identically. The distinction becomes observable only after `azd deploy --publish` (or equivalent) when each published agent gets its own MI. We keep both connections in the toolbox to teach the YAML/portal shape, even though the runtime behavior collapses pre-publish.

## Prerequisites

### 0. (Paths #2 and #3 only) Deploy Azure MCP Server to Azure Container Apps

Microsoft does not host a public Entra-MI-authenticated MCP server today, so paths #2 and #3 require a one-time deployment of the Azure MCP Server. Use the [`Azure-Samples/azmcp-foundry-aca-mi`](https://github.com/Azure-Samples/azmcp-foundry-aca-mi) Bicep template — it provisions:

- Azure Container Apps environment hosting the Azure MCP Server.
- An Entra app registration that authenticates the MCP server.
- Role assignments giving the **ACA managed identity** access to a target Azure resource (the template defaults to a Storage Account; substitute your own).

After deployment, save the ACA URL (e.g., `https://azure-mcp-storage-server.<random>.eastus2.azurecontainerapps.io`) and the Entra app's `ENTRA_APP_CLIENT_ID` — you'll use them when creating connections #2 and #3 below.

If you want to skip the Entra-MI paths entirely, omit tools #2 and #3 from your toolbox; the agent still works with just the remaining three paths.

### 1. Foundry project + Azure AI User role

- An active Microsoft Foundry project ([create one](https://learn.microsoft.com/en-us/azure/foundry/how-to/create-projects)).
- The **Azure AI User** role on the project assigned to:
  - The developer (you) creating the toolbox.
  - The agent identity (or project MI) for tool invocation.
  - Any **end user** that will run the OAuth path #4 — their tenant must match the project's tenant (cross-tenant token exchange is not supported).

### 2. Create the project connections in the Foundry portal

In the Foundry portal, open your project → Connections → Create connection. Repeat for each of these:

| Connection name (used by the toolbox) | Type to pick in wizard | Values |
|---|---|---|
| `github-mcp-key` | **Custom Keys** | Target: `https://api.githubcopilot.com/mcp` · Key name: `Authorization` · Value: `Bearer <your-github-pat>` |
| `azure-mcp-agent-id` | **Microsoft Entra ID** → Agent Identity | Target: your ACA URL from Step 0 · Audience: your `ENTRA_APP_CLIENT_ID` |
| `azure-mcp-project-mi` | **Microsoft Entra ID** → Project Managed Identity | Target: your ACA URL from Step 0 · Audience: your `ENTRA_APP_CLIENT_ID` |
| `outlook-mail-oauth` | **OAuth Identity Passthrough** → Custom OAuth | Target: discoverable from the **Add Tools** catalog (Outlook Mail Agent 365 server) · Scope: `ea9ffc3e-8a23-4a7d-836d-234d7c7565c1/McpServers.Mail.All` · Auth URL / Token URL / Refresh URL: `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/{authorize\|token}` |

> **Path #5** (`gitmcp.io`) needs no connection — the auth (a dummy bearer) lives on the toolbox tool entry itself.

### 3. Create the toolbox

In the Foundry portal → Tools → Add Toolbox. Name it `auth-paths-toolbox` (or whatever you prefer; export the name as `TOOLBOX_NAME`). Add five MCP tool entries:

| Tool `name:` | `server_url` | Auth |
|---|---|---|
| `github_pat` | `https://api.githubcopilot.com/mcp` | `project_connection_id: github-mcp-key` |
| `azure_mcp_agent` | Your ACA URL | `project_connection_id: azure-mcp-agent-id` |
| `azure_mcp_project` | Your ACA URL | `project_connection_id: azure-mcp-project-mi` |
| `outlook_mail` | (catalog-discovery URL) | `project_connection_id: outlook-mail-oauth` |
| `gitmcp_inline` | `https://gitmcp.io/Azure/azure-rest-api-specs` | `authorization: "Bearer demo-only-not-real"` (no `project_connection_id`) |

Each entry should also carry:

- `require_approval: never` (this sample is focused on auth, not approval flows; see [`ToolCallingApprovalHostedAgentFixture`](../../../../tests/Foundry.Hosting.IntegrationTests/) for that concern).
- A tight `allowed_tools` list. GitHub MCP exposes ~50 tools; restrict to what you actually want the model to invoke. For example: `github_pat` → `["search_issues", "get_issue", "list_pull_requests"]`.

### Sidebar — what the toolbox-creation code looks like

This sample assumes the toolbox already exists; it does not provision one programmatically. For an end-to-end code example of toolbox creation from a publisher script (suitable for a CI/CD pipeline), see [`02-agents/AgentsWithFoundry/Agent_Step25_FoundryToolboxMcp/Program.cs`](../../../../02-agents/AgentsWithFoundry/Agent_Step25_FoundryToolboxMcp/Program.cs) — its `CreateSampleToolboxAsync` helper uses `AgentAdministrationClient.GetAgentToolboxes().CreateToolboxVersionAsync(...)` and is the canonical pattern.

## Run the agent

Set environment variables (or copy `.env.example` to `.env` and fill it in):

```powershell
$env:AZURE_AI_PROJECT_ENDPOINT  = "https://<account>.services.ai.azure.com/api/projects/<project>"
$env:AZURE_AI_MODEL_DEPLOYMENT_NAME = "gpt-4o"
$env:TOOLBOX_NAME       = "auth-paths-toolbox"

# Local dev only — auto-injected in hosted containers
$env:FOUNDRY_AGENT_TOOLSET_ENDPOINT = "$env:AZURE_AI_PROJECT_ENDPOINT/toolboxes"
```

Then sign in (`az login`) and start the server:

```powershell
dotnet run --tl:off
```

The server logs at `http://localhost:8088/`. In a separate terminal, run the REPL client:

```powershell
cd ../Using-Samples/Hosted-Toolbox-AuthPaths-Client
dotnet run --tl:off
```

> **Parallel-run warning**: `Hosted-Toolbox/` and other `Hosted-*` samples default to the same port (8088) and the same agent name slot. Always set a unique `AGENT_NAME` (this sample defaults to `hosted-toolbox-auth-paths-agent`) and stop other hosted samples before starting this one.

## Sample prompts

One per auth path so each tool gets exercised at least once:

```
List the latest 3 issues in microsoft/agent-framework.            # path #1 — GitHub MCP (key)
List the storage accounts in my Azure subscription.               # path #2 — Azure MCP (agent identity)
List the storage containers in <account-name>.                    # path #3 — Azure MCP (project MI; pre-publish behaves like #2)
What's in my Outlook inbox today?                                 # path #4 — Outlook Mail (custom OAuth)
What's the latest API version for Microsoft.CognitiveServices?    # path #5 — gitmcp.io (inline Authorization)
```

For path #4, the first invocation will return an `mcp_approval_request` containing a Microsoft sign-in URL. The REPL client opens that URL in your default browser; complete consent and press Enter to resubmit. Subsequent invocations of the same tool succeed without prompting.

## Troubleshooting / partial-failure semantics

`AddFoundryToolboxes` resolves the toolbox at startup by listing its tools via MCP `tools/list`. The list comes back even when individual tool connections are misconfigured — failures only surface at tool-call time. Symptoms per auth path:

| Symptom | Likely cause |
|---|---|
| **HTTP 401/403** from a tool call | Path #1: PAT expired or scope insufficient. Path #2/#3: ACA managed identity missing role assignment on the target Azure resource. Path #4: end-user consent never completed for this user. |
| **`-32006` JSON-RPC error in the REPL output** | Path #4: OAuth consent required. The REPL should show the consent URL — open it and resubmit. If the URL is missing, check the `Hosted-Toolbox-AuthPaths-Client` log; the REPL extracts URLs heuristically. |
| **HTTP 404 from a tool call** | Toolbox name mismatch (`TOOLBOX_NAME` vs the name in the portal), or the toolbox was deleted. |
| **Server logs "FOUNDRY_AGENT_TOOLSET_ENDPOINT is not set; toolbox support is disabled"** | Local dev without the env var set. The agent will load with zero tools and respond as if it has none. Set the env var to `{AZURE_AI_PROJECT_ENDPOINT}/toolboxes`. |
| **Tools appear but model never invokes them** | `instructions:` in `Program.cs` may not surface what each tool is for. Tighten the `allowed_tools` lists and rephrase prompts to mention the upstream service by name. |

## Region and model compatibility

Foundry Toolboxes have region constraints; some tool types are limited to specific models. This sample defaults to `gpt-4o`, which works in all supported regions. For the full matrix, see the [Foundry tools compatibility matrix](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/tools/toolbox#region-and-model-compatibility).

## Anti-pattern note for path #5

Inline `authorization` on a toolbox tool entry stores credentials **inside the toolbox definition**. There is no rotation, no per-user scoping, no secret-store integration. Use it only for:

- Public MCP servers that ignore the bearer (the `gitmcp.io` case demonstrated here).
- Local development against a test MCP server with a throwaway token.

For everything else use `project_connection_id` and let the platform inject credentials.
