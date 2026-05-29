# Hosted Toolbox — Authentication Paths

A hosted Foundry agent backed by **two Foundry Toolboxes** that together bundle MCP tools using **four different authentication paths**. The educational surface lives in the toolbox configuration (which you provision via REST or the Foundry portal) and in this README. The agent code in `Program.cs` is small but introduces one important pattern beyond the basic [`Hosted-Toolbox/`](../Hosted-Toolbox/) sample: a **lazy-registered toolbox** for the OAuth path, so the agent boots even when the OAuth connection has not yet been consented.

The companion REPL client at [`Using-Samples/Hosted-Toolbox-AuthPaths-Client/`](../Using-Samples/Hosted-Toolbox-AuthPaths-Client/) drives the agent interactively across the four paths.

## What this sample teaches

| Aspect | This sample | Existing siblings |
|---|---|---|
| Toolbox marker pattern | `FoundryAITool.CreateHostedMcpToolbox(name)` + `AddFoundryToolboxes(name)` | Same as [`Hosted-Toolbox/`](../Hosted-Toolbox/) |
| Tools per toolbox | **Four MCP tools, each with a different auth method** | `Hosted-Toolbox/`: typically one demo tool |
| Consumption | Server-side (Foundry resolves the marker) | Same |
| Client | REPL with `oauth_consent_request` detection ([`Hosted-Toolbox-AuthPaths-Client/`](../Using-Samples/Hosted-Toolbox-AuthPaths-Client/)) | `Hosted-Toolbox/`: plain REPL |

Related samples:
- [`Hosted-Toolbox/`](../Hosted-Toolbox/) — simpler single-tool toolbox.
- [`Hosted-McpTools/`](../Hosted-McpTools/) — contrasts client-side `McpClient` vs server-side `HostedMcpServerTool` for non-toolbox MCP servers.

## Authentication-path matrix

The sample's purpose is to enumerate every authentication path a Foundry toolbox can drive, so each path appears alongside the others. Pick the ones your scenario needs — each connection in a toolbox is independent.

| # | Auth method | MCP target | Connection `authType` | What flows where | When to pick this |
|---|---|---|---|---|---|
| 1 | **Key-based via project connection** | GitHub MCP at `https://api.githubcopilot.com/mcp` | `CustomKeys` | A PAT stored as `Authorization: Bearer <pat>` lives in the Foundry connection. The toolbox proxy reads it server-side and injects on every MCP call. | The upstream service only accepts API keys or PATs. |
| 2 | **Microsoft Entra — agent identity** | Any Azure Cognitive Services MCP endpoint your project can reach (e.g., Language service MCP) | `AgenticIdentityToken` | Foundry mints an Entra token for the agent's own identity (`instance_identity` in the new agent object model), scoped to the connection's `audience`, and forwards it to the MCP server. The agent identity must hold the required role (typically `Cognitive Services User`) on the target resource. | Per-agent least-privilege access to Entra-protected services. Recommended default for Entra-protected MCP targets. |
| 4 | **Custom OAuth (Connection-stored token)** | GitHub MCP via Foundry's catalog `GitHub` OAuth2 connection | `OAuth2` | An **operator** authorizes the connection one time via Foundry portal → Connections → GitHub → Authorize. The OAuth refresh token is stored on the connection; every agent call to the OAuth tool reuses it server-side. | The upstream service exposes OAuth-only endpoints and you want a single shared identity managed by the Foundry connection. |
| 5 | **Inline `Authorization` (anti-pattern)** | `https://gitmcp.io/Azure/azure-rest-api-specs` | none | A literal bearer string lives on the toolbox tool entry's `authorization` field. **Do not do this in production** — there's no rotation, no secret store, no per-user identity. Shown for completeness. | Local-dev or public MCP servers that accept any (or no) bearer. |

> Path numbering preserves the index used by the companion REPL client (`--path 1|2|4|5`). Path 3 (`ProjectManagedIdentity`) is intentionally omitted from this sample — the Microsoft Entra agent identity flow (path 2) covers Entra-token-based access to downstream MCP servers and is the recommended path for new agents.

### Two toolboxes — why?

The sample uses two toolboxes:

- **Main toolbox** (`TOOLBOX_NAME`, e.g. `auth-paths-toolbox`) — paths 1, 2, 5. Loaded eagerly at startup. If any of these connections are misconfigured, the agent fails to start (fail-fast on the working paths).
- **OAuth toolbox** (`OAUTH_TOOLBOX_NAME`, e.g. `auth-paths-oauth-toolbox`) — path 4 only. Registered **lazily** (`AddFoundryLazyToolbox`): the MCP connection is opened on the first tool invocation, not at startup. This is the resilience pattern for OAuth: without it, an unconsented OAuth connection would fail the toolbox `tools/list` call at startup and the entire agent (all four paths) would stay 503 until an operator clicked Authorize.

> **Why not one toolbox?** Foundry's toolbox proxy lists all tools via a single `tools/list` call and returns a unified failure if any source errors. Splitting the OAuth source into its own toolbox and registering it lazily isolates the failure mode and keeps the other three paths available.

## Prerequisites

### 0. (Path #2 only) Identify an Entra-authenticated MCP target

Path #2 requires an MCP server that accepts Microsoft Entra tokens. Any **Azure Cognitive Services** resource that exposes an MCP endpoint works — they all accept Entra ID tokens and gate access via standard RBAC.

The reference walkthrough below uses an **Azure Language service** MCP endpoint:

```
https://<your-language-service>.cognitiveservices.azure.com/language/mcp?api-version=2025-11-15-preview
```

Substitute any other Cognitive Services MCP endpoint you have. If your project has none, omit tool #2 from your toolbox — the remaining three paths still work.

#### RBAC for path #2

Grant the **`Cognitive Services User`** role on the target resource to the agent's instance identity. Find it on the agent ARM resource (Azure portal → your agent → JSON view) at `instance_identity.principal_id`. This is the principal the Foundry proxy uses when minting tokens for `AgenticIdentityToken` connections.

```powershell
$lang = "/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<lang-svc>"

az role assignment create `
    --assignee-object-id <agent-instance-identity-principal-id> `
    --assignee-principal-type ServicePrincipal `
    --role "Cognitive Services User" `
    --scope $lang
```

Repeat for any additional Cognitive Services resources the principal needs to call.

> The RBAC grants require `Microsoft.Authorization/roleAssignments/write` on the target scope. In many enterprise subscriptions this needs a PIM JIT activation.

### 1. Foundry project + Azure AI User role

- An active Microsoft Foundry project ([create one](https://learn.microsoft.com/en-us/azure/foundry/how-to/create-projects)).
- The **Azure AI User** role on the project assigned to:
  - The developer (you) creating the toolbox.
  - The agent identity for tool invocation.
  - Any **end user** that will run the OAuth path #4 — their tenant must match the project's tenant (cross-tenant token exchange is not supported).

### 2. Create the project connections

The Entra-based connection (path #2) is not available in the Foundry portal connection wizard today. Create it via ARM REST:

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
| `GitHub` | (catalog OAuth connection) | `OAuth2` | n/a (scopes carry the OAuth resource) |

Path #5 (`gitmcp.io`) needs no connection — the auth lives on the toolbox tool entry itself.

The `audience` value is the OAuth resource identifier of the target service — for any Cognitive Services resource it is `https://cognitiveservices.azure.com`. For other Azure services consult [Agent identity — runtime token exchange](https://learn.microsoft.com/azure/foundry/agents/concepts/agent-identity#runtime-token-exchange).

#### Authorize the OAuth connection (path #4) — one-time operator step

In the Foundry portal:

1. Open **Management center → Connected resources** for your project.
2. Find the `GitHub` connection (catalog-created OAuth2). If it isn't there, add it from the catalog.
3. Click **Edit authentication** → **Authorize**. Sign in to GitHub and grant consent.

After this one-time step the connection stores a refresh token; the agent's MI uses it server-side for every subsequent path-#4 call. Without this consent, the toolbox proxy returns JSON-RPC error `-32007` (`CONSENT_REQUIRED`) when the OAuth toolbox is opened; the Foundry hosting bridge surfaces this to the client as an [`oauth_consent_request`](../Using-Samples/Hosted-Toolbox-AuthPaths-Client/README.md) output item followed by `response.incomplete`. The OAuth toolbox is registered **lazily** in this sample so the failure is isolated to the first OAuth invocation — see [Two toolboxes — why?](#two-toolboxes--why).

> The consent is bound to the connection, not to the consenting user. Once any authorized operator has consented, the connection is usable by the agent's managed identity. There is no per-end-user consent flow for toolbox-managed OAuth tools today.

### 3. Create the toolboxes

Provision two toolboxes via REST (`POST {project}/toolboxes/{name}/versions?api-version=v1` with header `Foundry-Features: Toolboxes=V1Preview`; the toolbox is implicitly created on first version POST). Then PATCH the toolbox with `{"default_version":"<n>"}` (`Content-Type: application/merge-patch+json`) to make it the active version.

**Main toolbox** — `auth-paths-toolbox` (or whatever you export as `TOOLBOX_NAME`). Three MCP tool entries:

| Tool `server_label` | `server_url` | Auth |
|---|---|---|
| `github_pat` | `https://api.githubcopilot.com/mcp` | `project_connection_id: github-mcp-key` |
| `lang_agent` | Your Language service MCP URL | `project_connection_id: lang-mcp-agent-id` |
| `gitmcp_inline` | `https://gitmcp.io/Azure/azure-rest-api-specs` | `authorization: "Bearer demo-only-not-real"` (no `project_connection_id`) |

**OAuth toolbox** — `auth-paths-oauth-toolbox` (or whatever you export as `OAUTH_TOOLBOX_NAME`). One MCP tool entry:

| Tool `server_label` | `server_url` | Auth |
|---|---|---|
| `github_oauth` | `https://api.githubcopilot.com/mcp` | `project_connection_id: GitHub` |

Each entry should also carry:

- `require_approval: never` (this sample is focused on auth, not approval flows; see [`ToolCallingApprovalHostedAgentFixture.cs`](../../../../../tests/Foundry.Hosting.IntegrationTests/Fixtures/ToolCallingApprovalHostedAgentFixture.cs) for that concern).
- A tight `allowed_tools` list. GitHub MCP exposes ~50 tools; restrict to what you actually want the model to invoke. For example: `github_pat` → `["search_issues", "issue_read", "list_pull_requests"]`; `github_oauth` → `["search_issues", "issue_read", "get_me"]`. Tool names must match the upstream `tools/list` exactly (use `issue_read`, not `get_issue`).

### Sidebar — what the toolbox-creation code looks like

This sample assumes the toolbox already exists; it does not provision one programmatically. For an end-to-end code example of toolbox creation from a publisher script (suitable for a CI/CD pipeline), see [`02-agents/AgentsWithFoundry/Agent_Step25_FoundryToolboxMcp/Program.cs`](../../../../02-agents/AgentsWithFoundry/Agent_Step25_FoundryToolboxMcp/Program.cs) — its `CreateSampleToolboxAsync` helper uses `AgentAdministrationClient.GetAgentToolboxes().CreateToolboxVersionAsync(...)` and is the canonical pattern.

## Run the agent

Set environment variables (or copy `.env.example` to `.env` and fill it in):

```powershell
$env:AZURE_AI_PROJECT_ENDPOINT  = "https://<account>.services.ai.azure.com/api/projects/<project>"
$env:AZURE_AI_MODEL_DEPLOYMENT_NAME = "gpt-4o"
$env:TOOLBOX_NAME               = "auth-paths-toolbox"
$env:OAUTH_TOOLBOX_NAME         = "auth-paths-oauth-toolbox"
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
Detect the language of "Bonjour le monde".                        # path #2 — Language MCP (agent identity)
Using github_oauth___get_me, return my GitHub user profile.       # path #4 — GitHub MCP (OAuth connection)
What's the latest API version for Microsoft.CognitiveServices?    # path #5 — gitmcp.io (inline Authorization)
```

For path #4, behavior depends on whether the `GitHub` connection has been authorized (see [Authorize the OAuth connection](#authorize-the-oauth-connection-path-4--one-time-operator-step)). If authorized, the tool returns the GitHub user profile. If not, the call returns a generic tool failure and the model surfaces something like "I couldn't retrieve your GitHub profile because the tool failed" — that's the signal the operator still needs to authorize the connection.

## Troubleshooting / partial-failure semantics

`AddFoundryToolboxes` resolves the toolbox at startup by listing its tools via MCP `tools/list`. The list comes back even when individual tool connections are misconfigured — failures only surface at tool-call time. Symptoms per auth path:

| Symptom | Likely cause |
|---|---|
| **HTTP 401 "audience is incorrect"** | The connection's `audience` field is missing or does not match the OAuth resource identifier the target service accepts. For Cognitive Services targets, set `audience: "https://cognitiveservices.azure.com"`. |
| **HTTP 401 / 403 "principal does not have access"** | Path #1: PAT expired or scope insufficient. Path #2: the agent's instance identity is missing the required role on the target resource. |
| **Path #4 returns `"Error: Function failed."` with no other detail** | The `GitHub` connection has not been authorized yet. Go to the Foundry portal → Management center → Connected resources → `GitHub` → **Edit authentication** → **Authorize**, sign in to GitHub, and retry. The OAuth toolbox is registered lazily so this failure mode does not block the other three paths. |
| **Container reports zero tools but startup succeeded** | `FoundryToolboxService.StartAsync` caches the eager toolbox's `tools/list` result at startup. If a connection or RBAC grant changed after the container started, force a fresh container (re-deploy the agent version) — the cache won't pick up the change until then. |
| **HTTP 404 from a tool call** | Toolbox name mismatch (`TOOLBOX_NAME` / `OAUTH_TOOLBOX_NAME` vs the names in the portal), or the toolbox was deleted. |
| **Server logs "FOUNDRY_PROJECT_ENDPOINT is not set; toolbox support is disabled"** | Local dev without the env var set. The agent will load with zero tools and respond as if it has none. Set `AZURE_AI_PROJECT_ENDPOINT` (local-dev fallback) or `FOUNDRY_PROJECT_ENDPOINT` to your project endpoint. |
| **Tools appear but model never invokes them** | `instructions:` in `Program.cs` may not surface what each tool is for. Tighten the `allowed_tools` lists and rephrase prompts to mention the upstream service and tool name (e.g. `github_oauth___get_me`). |

## Region and model compatibility

Foundry Toolboxes have region constraints; some tool types are limited to specific models. This sample defaults to `gpt-4o`, which works in all supported regions. For the full matrix, see the [Foundry tools compatibility matrix](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/tools/toolbox#region-and-model-compatibility).

## Anti-pattern note for path #5

Inline `authorization` on a toolbox tool entry stores credentials **inside the toolbox definition**. There is no rotation, no per-user scoping, no secret-store integration. Use it only for:

- Public MCP servers that ignore the bearer (the `gitmcp.io` case demonstrated here).
- Local development against a test MCP server with a throwaway token.

For everything else use `project_connection_id` and let the platform inject credentials.
