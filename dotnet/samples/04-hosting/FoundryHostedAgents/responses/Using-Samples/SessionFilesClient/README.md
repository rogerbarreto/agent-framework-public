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

To chat with the agent against this session, run:
  azd ai agent invoke --session-id f23a... "<your prompt>"

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
| `help` | Show command reference. |
| `quit` | Delete the session and exit. |

## End-to-end demo

Pair this REPL with a deployed `hosted-files` agent:

```text
files> upload ../../Hosted-Files/resources/contoso_q1_2026_report.txt
Uploaded 6145 bytes to /home/contoso_q1_2026_report.txt

files> ls
.:
   <DIR>  .
      6145  contoso_q1_2026_report.txt
```

Then in another terminal, ask the agent (using the session id printed at startup):

```bash
azd ai agent invoke --session-id <session-id> \
  "Read contoso_q1_2026_report.txt from \$HOME and quote the headline revenue figure verbatim."
```

The agent's `ReadFile` tool resolves the relative path against `$HOME`, reads the file uploaded above, and quotes the figure (`$1,482.6M`).

## How it works

1. `AgentAdministrationClient` is built with a `Foundry-Features: HostedAgents=V1Preview,AgentEndpoints=V1Preview` header (required for the alpha API).
2. The latest agent version is resolved (`GetAgentVersionsAsync`).
3. A session is created with `CreateSessionAsync(agentName, isolationKey, versionIndicator, agentSessionId)`. The isolation key is held by the REPL and required for session-mutating operations (notably `DeleteSession`).
4. `agentsClient.GetAgentSessionFiles()` returns the `AgentSessionFiles` client used for `UploadSessionFileAsync`, `GetSessionFilesAsync`, `DownloadSessionFileAsync`, and `DeleteSessionFileAsync`.
5. On `quit`, the REPL deletes the session.

## CLI parity

The same flow with the Azure Developer CLI:

```bash
azd ai agent invoke "Hi"                              # creates a session implicitly
azd ai agent files upload -f contoso_q1_2026_report.txt
azd ai agent invoke "Read contoso_q1_2026_report.txt from \$HOME and quote the headline revenue figure verbatim."
```

`azd` auto-detects the most recent active session for upload. The REPL above gives you explicit session control via the SDK.
