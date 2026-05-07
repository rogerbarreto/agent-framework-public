# SessionFilesClient

A console REPL that exercises the alpha **`Azure.AI.Projects.AgentSessionFiles`** API to manage files inside a Foundry hosted-agent session sandbox (`$HOME`). The code-first equivalent of `azd ai agent files upload`.

Use this with the [`Hosted-Files`](../../Hosted-Files/) sample agent.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A deployed Hosted-Files agent in your Azure AI Foundry project. See [Hosted-Files/README.md](../../Hosted-Files/README.md) for deployment instructions.
- Azure CLI logged in (`az login`)

## Configuration

```env
FOUNDRY_PROJECT_ENDPOINT=https://<your-account>.services.ai.azure.com/api/projects/<your-project>
HOSTED_AGENT_NAME=hosted-files
# Optional - defaults to the latest deployed version
# HOSTED_AGENT_VERSION=1
```

Place the values in a `.env` file at the project root or export them as environment variables.

## Run

```bash
cd dotnet/samples/04-hosting/FoundryHostedAgents/responses/Using-Samples/SessionFilesClient
dotnet run
```

On startup the REPL creates a fresh session, waits for it to become `Active`, prints the `sessionId`, and drops you at a prompt:

```
══════════════════════════════════════════════════════════
Session Files REPL
Agent:      hosted-files
Session:    f23a...
Isolation:  9c8b...

Type 'help' for commands, 'quit' to delete the session and exit.
══════════════════════════════════════════════════════════

files>
```

## Commands

| Command | Description |
|---------|-------------|
| `upload <local> [<remote>]` | Upload a local file into the session sandbox. Default `<remote>` = file name. |
| `ls [<path>]` | List entries at the given session path (default `"."`). |
| `download <remote> <local>` | Download a session file locally. |
| `rm <remote>` | Delete a session file. |
| `ask <prompt>` | Send a prompt to the agent. The request body is pinned to this REPL's `agent_session_id` (via `CreateResponseOptions.Patch`) so the agent container reads files this REPL uploaded. |
| `help` | Show command reference. |
| `quit` | Delete the session and exit. |

## End-to-end demo (file → agent knowledge)

This is the canonical flow the sample is designed to show: a file uploaded by the client surfaces in the agent's response as knowledge it retrieved through its container-side `ReadFile` tool.

```text
files> upload ../../Hosted-Files/resources/contoso_q1_2026_report.txt
Uploaded 6145 bytes to contoso_q1_2026_report.txt

files> ls
.:
      6145  contoso_q1_2026_report.txt

files> ask Read contoso_q1_2026_report.txt from $HOME and quote the headline total revenue figure verbatim, no commentary.
Agent> Total revenue of $1,482.6M.

files> quit
Deleting session ...
Session deleted.
```

The `ask` request hits the deployed Hosted-Files agent over the same `agent_session_id` the upload used. The agent's `ReadFile` tool reads `$HOME/contoso_q1_2026_report.txt`, the model quotes `$1,482.6M` verbatim from the file.

## How it works

1. `AgentAdministrationClient` is built with a `Foundry-Features: HostedAgents=V1Preview,AgentEndpoints=V1Preview` header (required for the alpha API).
2. The latest agent version is resolved (`GetAgentVersionsAsync`).
3. A session is created with `CreateSessionAsync(agentName, isolationKey, versionIndicator, agentSessionId)`. The isolation key is held by the REPL and required for session-mutating operations (notably `DeleteSession`).
4. `agentsClient.GetAgentSessionFiles()` returns the `AgentSessionFiles` client used for `UploadSessionFileAsync`, `GetSessionFilesAsync`, `DownloadSessionFileAsync`, and `DeleteSessionFileAsync`.
5. A per-agent `ProjectResponsesClient` is built with `ProjectOpenAIClientOptions { AgentName = ... }` so requests target `/agents/{name}/endpoint/protocols/openai`. The `ask` command pins `agent_session_id` into the request body via `CreateResponseOptions.Patch.Set("$.agent_session_id"u8, ...)` so the inference call lands in the same container the files were uploaded to.
6. On `quit`, the REPL deletes the session.

## CLI parity

The same flow with the Azure Developer CLI:

```bash
azd ai agent invoke "Hi"                              # creates a session implicitly
azd ai agent files upload -f contoso_q1_2026_report.txt
azd ai agent invoke "Read contoso_q1_2026_report.txt from \$HOME and quote the headline revenue figure verbatim."
```

`azd` auto-detects the most recent active session for upload. The REPL above gives you explicit session control via the SDK and a single in-process loop covering both upload and chat.
