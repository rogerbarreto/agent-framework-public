# Hosted Toolbox — Authentication Paths

A hosted Foundry agent backed by a single Foundry Toolbox that bundles MCP tools using **four different authentication paths**. The educational surface lives in the toolbox configuration (which you provision in the Foundry portal) and in this README — the agent code itself is identical to the existing [`Hosted-Toolbox/`](../Hosted-Toolbox/) sample.

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

| # | Auth method | MCP target | Connection `authType` | What flows where |
|---|---|---|---|---|
| 1 | **Key-based via project connection** | GitHub MCP at `https://api.githubcopilot.com/mcp` | `CustomKeys` | A PAT stored as `Authorization: Bearer <pat>` lives in the Foundry connection. The toolbox proxy reads it server-side and injects on every MCP call. |
| 2 | **Microsoft Entra — agent identity** | Any Azure Cognitive Services MCP endpoint your project can reach (e.g., Language service MCP) | `AgenticIdentityToken` | Foundry mints an Entra token for the agent's own identity (`instance_identity` in the new agent object model), scoped to the connection's `audience`, and forwards it to the MCP server. The agent identity must hold the required role (typically `Cognitive Services User`) on the target resource. |
| 4 | **Custom OAuth (identity passthrough)** | Outlook Mail Agent 365 server (catalog discovery) | `OAuth2` | The end user signs in to the OAuth app. Their token (scoped to `McpServers.Mail.All`) is used per request. First request emits an `mcp_approval_request` with the consent URL. |
| 5 | **Inline `Authorization` (anti-pattern)** | `https://gitmcp.io/Azure/azure-rest-api-specs` | none | A literal bearer string lives on the toolbox tool entry's `authorization` field. **Do not do this in production** — there's no rotation, no secret store, no per-user identity. Shown for completeness. |

> **Project managed identity** (`authType: ProjectManagedIdentity`) is a separate Entra-based connection type also accepted by the Foundry connection schema. It mints tokens for the project's system-assigned MI rather than the agent identity. It is out of scope for this sample because the new agent object model gives every agent its own `instance_identity` from the moment it's created, making per-agent identity (path #2) the recommended approach for new agents. Use `ProjectManagedIdentity` when multiple agents need to share the same access level for the same downstream resource.

## Prerequisites

### 0. (Path #2 only) Identify an Entra-authenticated MCP target

Path #2 requires an MCP server that accepts Microsoft Entra tokens. Any **Azure Cognitive Services** resource that exposes an MCP endpoint works — they all accept Entra ID tokens and gate access via standard RBAC.

The reference walkthrough below uses an **Azure Language service** MCP endpoint:

```
https://<your-language-service>.cognitiveservices.azure.com/language/mcp?api-version=2025-11-15-preview
```

Substitute any other Cognitive Services MCP endpoint you have. If your project has none, omit tool #2 from your toolbox — the remaining three paths still work.

#### RBAC for path #2

Find your agent's instance identity on the agent ARM resource (Azure portal → your agent → JSON view, or via REST). It is at `instance_identity.principal_id`. This is the principal that the Foundry proxy uses when minting tokens for `AgenticIdentityToken` connections.

Grant the **`Cognitive Services User`** role on the target resource to that principal:

```powershell
$lang = "/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<lang-svc>"
az role assignment create `
    --assignee-object-id <agent-instance-identity-principal-id> `
    --assignee-principal-type ServicePrincipal `
    --role "Cognitive Services User" `
    --scope $lang
```

Repeat for any additional Cognitive Services resources the agent needs to call.

> The RBAC grant requires `Microsoft.Authorization/roleAssignments/write` on the target scope. In many enterprise subscriptions this needs a PIM JIT activation.

### 1. Foundry project + Azure AI User role

- An active Microsoft Foundry project ([create one](https://learn.microsoft.com/en-us/azure/foundry/how-to/create-projects)).
- The **Azure AI User** role on the project assigned to:
  - The developer (you) creating the toolbox.
  - The agent identity for tool invocation.
  - Any **end user** that will run the OAuth path #4 — their tenant must match the project's tenant (cross-tenant token exchange is not supported).

### 2. Create the project connections

The Entra-based connection for path #2 is not available in the Foundry portal connection wizard today. Create it via ARM REST:

```powershell
$armToken = az account get-access-token --query accessToken -o tsv
$h        = @{ Authorization = "Bearer $armToken"; "Content-Type" = "application/json" }
$proj     = "/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<foundry-account>/projects/<project>"
$lang     = "https://<lang-svc>.cognitiveservices.azure.com/language/mcp?api-version=2025-11-15-preview"

# Path 2 — agent identity
$body2 = @{ properties = @{
    category = "RemoteTool"; target = $lang
    authType = "AgenticIdentityToken"; audience = "https://cognitiveservices.azure.com"
    isSharedToAll = $false
}} | ConvertTo-Json -Depth 5
az rest --method PUT --headers "Content-Type=application/json" `
    --url "https://management.azure.com$proj/connections/lang-mcp-agent-id?api-version=2025-04-01-preview" `
    --body $body2
```

Connection summary:

| Connection name (used by the toolbox) | `category` | `authType` | `audience` |
|---|---|---|---|
| `github-mcp-key` | `CustomKeys` | `CustomKeys` | n/a (key value carries `Authorization: Bearer <pat>`) |
| `lang-mcp-agent-id` | `RemoteTool` | `AgenticIdentityToken` | `https://cognitiveservices.azure.com` |
| `outlook-mail-oauth` | `RemoteTool` | `OAuth2` | n/a (scopes carry the OAuth resource) |

Path #5 (`gitmcp.io`) needs no connection — the auth lives on the toolbox tool entry itself.

The `audience` value is the OAuth resource identifier of the target service — for any Cognitive Services resource it is `https://cognitiveservices.azure.com`. For other Azure services consult [Agent identity — runtime token exchange](https://learn.microsoft.com/azure/foundry/agents/concepts/agent-identity#runtime-token-exchange).

### 3. Create the toolbox

In the Foundry portal → Tools → Add Toolbox. Name it `auth-paths-toolbox` (or whatever you prefer; export the name as `TOOLBOX_NAME`). Add four MCP tool entries:

| Tool `server_label` | `server_url` | Auth |
|---|---|---|
| `github_pat` | `https://api.githubcopilot.com/mcp` | `project_connection_id: github-mcp-key` |
| `lang_agent` | Your Language service MCP URL | `project_connection_id: lang-mcp-agent-id` |
| `outlook_mail` | (catalog-discovery URL) | `project_connection_id: outlook-mail-oauth` |
| `gitmcp_inline` | `https://gitmcp.io/Azure/azure-rest-api-specs` | `authorization: "Bearer demo-only-not-real"` (no `project_connection_id`) |

Each entry should also carry:

- `require_approval: never` (this sample is focused on auth, not approval flows; see [`ToolCallingApprovalHostedAgentFixture`](../../../../tests/Foundry.Hosting.IntegrationTests/) for that concern).
- A tight `allowed_tools` list. GitHub MCP exposes ~50 tools; restrict to what you actually want the model to invoke. For example: `github_pat` → `["search_issues", "get_issue", "list_pull_requests"]`.

### Sidebar — what the toolbox-creation code looks like

This sample assumes the toolbox already exists; it does not provision one programmatically. For an end-to-end code example of toolbox creation from a publisher script (suitable for a CI/CD pipeline), see [`02-agents/AgentsWithFoundry/Agent_Step25_FoundryToolboxMcp/Program.cs`](../../../../02-agents/AgentsWithFoundry/Agent_Step25_FoundryToolboxMcp/Program.cs) — its `CreateSampleToolboxAsync` helper uses `AgentAdministrationClient.GetAgentToolboxes().CreateToolboxVersionAsync(...)` and is the canonical pattern.

## Run the agent

Set environment variables (or copy `.env.example` to `.env` and fill it in):

Set environment variables (or copy `.env.example` to `.env` and fill it in):

```powershell
$env:AZURE_AI_PROJECT_ENDPOINT  = "https://<account>.services.ai.azure.com/api/projects/<project>"
$env:AZURE_AI_MODEL_DEPLOYMENT_NAME = "gpt-4o"
$env:TOOLBOX_NAME       = "auth-paths-toolbox"
```

Locally, the `Foundry.Hosting` package reads `AZURE_AI_PROJECT_ENDPOINT` as a fallback when `FOUNDRY_PROJECT_ENDPOINT` is absent. In the hosted Foundry runtime, the platform auto-injects `FOUNDRY_PROJECT_ENDPOINT` and the package builds the toolbox proxy URL as `{FOUNDRY_PROJECT_ENDPOINT}/toolboxes/{TOOLBOX_NAME}/mcp?api-version=v1` per [`tools-integration-spec.md`](https://github.com/microsoft/AgentSchema/blob/main/specs/agents/hosted_agents/container-spec/docs/tools-integration-spec.md) §2–§3.

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
Detect the language of "Bonjour le monde, comment allez-vous?".   # path #2 — Language MCP (agent identity)
What's in my Outlook inbox today?                                 # path #4 — Outlook Mail (custom OAuth)
What's the latest API version for Microsoft.CognitiveServices?    # path #5 — gitmcp.io (inline Authorization)
```

For path #4, the first invocation will return an `mcp_approval_request` containing a Microsoft sign-in URL. The REPL client opens that URL in your default browser; complete consent and press Enter to resubmit. Subsequent invocations of the same tool succeed without prompting.

## Troubleshooting / partial-failure semantics

`AddFoundryToolboxes` resolves the toolbox at startup by listing its tools via MCP `tools/list`. The list comes back even when individual tool connections are misconfigured — failures only surface at tool-call time. Symptoms per auth path:

| Symptom | Likely cause |
|---|---|
| **HTTP 401 "audience is incorrect"** | The connection's `audience` field is missing or does not match the OAuth resource identifier the target service accepts. For Cognitive Services targets, set `audience: "https://cognitiveservices.azure.com"`. |
| **HTTP 401 / 403 "principal does not have access"** | Path #1: PAT expired or scope insufficient. Path #2: the agent's instance identity is missing the required role on the target resource. Path #4: end-user consent never completed for this user. |
| **Container reports zero tools but startup succeeded** | `FoundryToolboxService.StartAsync` caches the `tools/list` result at startup. If a connection or RBAC grant changed after the container started, force a fresh container (re-deploy the agent version) — the cache won't pick up the change until then. |
| **`-32006` JSON-RPC error in the REPL output** | Path #4: OAuth consent required. The REPL should show the consent URL — open it and resubmit. If the URL is missing, check the `Hosted-Toolbox-AuthPaths-Client` log; the REPL extracts URLs heuristically. |
| **HTTP 404 from a tool call** | Toolbox name mismatch (`TOOLBOX_NAME` vs the name in the portal), or the toolbox was deleted. |
| **Server logs "FOUNDRY_PROJECT_ENDPOINT is not set; toolbox support is disabled"** | Local dev without the env var set. The agent will load with zero tools and respond as if it has none. Set `AZURE_AI_PROJECT_ENDPOINT` (local-dev fallback) or `FOUNDRY_PROJECT_ENDPOINT` to your project endpoint. |
| **Tools appear but model never invokes them** | `instructions:` in `Program.cs` may not surface what each tool is for. Tighten the `allowed_tools` lists and rephrase prompts to mention the upstream service by name. |

## Region and model compatibility

Foundry Toolboxes have region constraints; some tool types are limited to specific models. This sample defaults to `gpt-4o`, which works in all supported regions. For the full matrix, see the [Foundry tools compatibility matrix](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/tools/toolbox#region-and-model-compatibility).

## Anti-pattern note for path #5

Inline `authorization` on a toolbox tool entry stores credentials **inside the toolbox definition**. There is no rotation, no per-user scoping, no secret-store integration. Use it only for:

- Public MCP servers that ignore the bearer (the `gitmcp.io` case demonstrated here).
- Local development against a test MCP server with a throwaway token.

For everything else use `project_connection_id` and let the platform inject credentials.
