# Hosted-Files

A hosted agent that reads files from the **per-session `$HOME` sandbox volume**. Each Foundry hosted-agent session is backed by an isolated micro-VM with its own persistent `$HOME`. Files uploaded to a session appear there and can be read by tools running inside the agent process.

The agent exposes three local C# function tools:

| Tool | Description |
|------|-------------|
| `GetHomeDirectory` | Returns the absolute path of `$HOME` for the current session. |
| `ListFiles` | Lists files and directories under a given path inside the sandbox. |
| `ReadFile` | Reads the full text contents of a file inside the sandbox. |

Companion sample: [`Using-Samples/SessionFilesClient`](../Using-Samples/SessionFilesClient/) — a REPL that uploads, lists, downloads, and deletes session files using the alpha `Azure.AI.Projects.AgentSessionFiles` SDK, then chats with this agent.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An Azure AI Foundry project with a deployed model (e.g., `gpt-4o`)
- Azure CLI logged in (`az login`)

## Configuration

Copy the template and fill in your project endpoint:

```bash
cp .env.example .env
```

Edit `.env`:

```env
AZURE_AI_PROJECT_ENDPOINT=https://<your-account>.services.ai.azure.com/api/projects/<your-project>
ASPNETCORE_URLS=http://+:8088
ASPNETCORE_ENVIRONMENT=Development
AZURE_AI_MODEL_DEPLOYMENT_NAME=gpt-4o
```

> `.env` is gitignored. The `.env.example` template is checked in as a reference.

## Running directly (contributors)

```bash
cd dotnet/samples/04-hosting/FoundryHostedAgents/responses/Hosted-Files
AGENT_NAME=hosted-files dotnet run
```

The agent starts on `http://localhost:8088`.

## Sending files to the agent

Files must be uploaded into the session's `$HOME` before the agent can read them. The bundled sample file is [`resources/contoso_q1_2026_report.txt`](./resources/contoso_q1_2026_report.txt).

### Code-first (recommended for demos)

Use the companion REPL [`SessionFilesClient`](../Using-Samples/SessionFilesClient/), which exercises the alpha `Azure.AI.Projects.AgentSessionFiles` SDK directly:

```bash
cd ../Using-Samples/SessionFilesClient
AGENT_NAME=hosted-files AGENT_ENDPOINT=http://localhost:8088 dotnet run

> upload ../../Hosted-Files/resources/contoso_q1_2026_report.txt
> ls
> What was Contoso's Q1 2026 total revenue? Quote the figure verbatim.
```

### CLI-first (parity with Python sample)

Using the Azure Developer CLI:

```bash
azd ai agent invoke "Hi!"          # creates a session
azd ai agent files upload -f resources/contoso_q1_2026_report.txt
azd ai agent invoke "What was Contoso's Q1 2026 total revenue? Quote the figure verbatim."
```

The `--session-id` flag selects a specific session; without it the CLI uploads to the most recently active session. Run `azd ai agent files upload -h` for the full set of options.

## Running with Docker

This project uses `ProjectReference`, so use `Dockerfile.contributor` which takes a pre-published output:

```bash
dotnet publish -c Debug -f net10.0 -r linux-musl-x64 --self-contained false -o out
docker build -f Dockerfile.contributor -t hosted-files .

export AZURE_BEARER_TOKEN=$(az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv)
docker run --rm -p 8088:8088 \
  -e AGENT_NAME=hosted-files \
  -e AZURE_BEARER_TOKEN=$AZURE_BEARER_TOKEN \
  --env-file .env \
  hosted-files
```

## NuGet package users

If consuming the Agent Framework as a NuGet package, use the standard `Dockerfile` instead of `Dockerfile.contributor` and switch the `ProjectReference` entries in `HostedFiles.csproj` to `PackageReference` (commented section in the csproj).

## How session files work

| Layer | Lifetime | Notes |
|-------|----------|-------|
| `$HOME` | Lifetime of the session (TTL: 30 days) | Persists across invocations within a session. |
| `/tmp` | Container process | Use for non-persistent scratch space. |
| Conversation | Indefinite | Stored separately from `$HOME`. |

Each Foundry hosted-agent session = one container = one `$HOME`. Files uploaded with `AgentSessionFiles.UploadSessionFileAsync(agentName, sessionId, sessionStoragePath, localPath)` land at `$HOME/<sessionStoragePath>`. The agent's tools resolve relative paths against `$HOME`.
