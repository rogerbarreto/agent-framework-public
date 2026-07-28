# Hosted-ChatClientAgent

A minimal general-purpose AI assistant hosted as a Foundry Hosted Agent using the Responses protocol. The agent is created inline via `AIProjectClient.AsAIAgent(model, instructions)` and served with `AddFoundryResponses` / `MapFoundryResponses`.

This sample deploys to Foundry **directly from source (code / ZIP upload)**: the platform builds and runs your code with no container image, so there is no Dockerfile to author or container registry to manage.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A Foundry project with a deployed model (for example `gpt-4o`)
- Azure CLI logged in (`az login`)
- Azure Developer CLI (`azd`) with the AI agents extension: `azd extension install azure.ai.agents`

## Files

| File | Purpose |
|------|---------|
| `Program.cs` | The agent: builds the agent, hosts it with the Responses protocol. |
| `azure.yaml` | The unified `azd` project file. Declares the Foundry project and the hosted agent with `codeConfiguration` (source/ZIP deploy). |
| `.agentignore` | Controls which files are excluded from the code-deploy ZIP upload (`.gitignore` syntax). |
| `HostedChatClientAgent.csproj` | Self-contained project: single target framework, central package management off, explicit package versions (there is no repo-level props file inside the ZIP). |
| `Directory.Packages.props` | Stops inheritance of the repository's central package management so the in-repo build matches the server-side build of the uploaded ZIP. |
| `.env.example` | Template for local configuration. |

## Configuration

Copy the template and fill in your project endpoint:

```bash
cp .env.example .env
```

```env
FOUNDRY_PROJECT_ENDPOINT=https://<your-account>.services.ai.azure.com/api/projects/<your-project>
AZURE_AI_MODEL_DEPLOYMENT_NAME=gpt-4o
AZURE_TOKEN_CREDENTIALS=dev
```

> `.env` is gitignored. The `.env.example` template is checked in as a reference.

> **Local development on a machine without a managed identity:** set `AZURE_TOKEN_CREDENTIALS=dev`.
> `Program.cs` authenticates with `DefaultAzureCredential` (the pattern the hosted platform
> expects, where a managed identity is injected). On a developer machine with no managed identity,
> `DefaultAzureCredential` probes the Azure Instance Metadata Service (IMDS, `169.254.169.254`) and
> blocks for a long time on the network timeout before every model call, so requests appear to
> hang. Setting `AZURE_TOKEN_CREDENTIALS=dev` restricts `DefaultAzureCredential` to developer
> credentials (Azure CLI, Visual Studio, `azd`) and skips the managed-identity probe. This variable
> is only for local runs; the deployed agent in Foundry uses the platform-injected managed identity.

## Run and test locally

Local runs use two terminals: one hosts the agent, the other is a code-first client that talks to it
using Agent Framework components, see the sibling [`Using-Samples`](../Using-Samples/) REPLs.

`AddFoundryResponses` binds the app to the port Foundry probes for readiness (8088 by default,
overridable with the `PORT` environment variable), and `MapFoundryResponses` serves the standard
`POST /responses` route. That is the same route the platform routes to for a deployed agent, so the
local server needs no extra wiring: the client just points an OpenAI responses client at
`http://localhost:8088`.

**Terminal 1 — host the agent:**

```bash
cd dotnet/samples/04-hosting/FoundryHostedAgents/responses/Hosted-ChatClientAgent
az login
dotnet run
```

The agent starts on `http://localhost:8088`.

**Terminal 2 — chat with it (code-first REPL):**

```powershell
cd dotnet/samples/04-hosting/FoundryHostedAgents/responses/Using-Samples/SimpleAgent
$env:AZURE_AI_AGENT_NAME = "hosted-chat-client-agent"
dotnet run
```

The REPL asks which agent to chat with; choose **2 (Local)**. It then points an OpenAI responses
client at the local server and streams the reply.

## Deploy to Foundry (source / ZIP)

The Azure Developer CLI scaffolds the project into a working folder, so run `init` from an empty
directory outside the repo and point `-m` at this sample's `azure.yaml`:

```powershell
mkdir <work-dir>; cd <work-dir>
azd auth login
azd ai agent init -m <repo>/dotnet/samples/04-hosting/FoundryHostedAgents/responses/Hosted-ChatClientAgent/azure.yaml `
  -p <project-id> -d <model-deployment>
cd hosted-chat-client-agent
azd provision
azd deploy
```

`azd` packages the source into a ZIP (honoring `.agentignore`), uploads it, and Foundry runs
`dotnet restore` + `dotnet publish` on it during provisioning (`dependencyResolution: remote_build`
in `azure.yaml`). No Dockerfile, no container registry.

Test the deployed agent with the REPL, choosing **1 (Foundry)** at the prompt:

```powershell
cd <repo>/dotnet/samples/04-hosting/FoundryHostedAgents/responses/Using-Samples/SimpleAgent
$env:FOUNDRY_PROJECT_ENDPOINT = "https://<your-account>.services.ai.azure.com/api/projects/<your-project>"
$env:AZURE_AI_AGENT_NAME      = "hosted-chat-client-agent"
dotnet run
```

Or use the `azd` shortcut: `azd ai agent invoke "Hello!"`.

## Deploy your local framework changes (contributors)

By default the project restores the published Agent Framework packages, so local changes to the
framework are not exercised. To deploy them, stage the sample first:

```powershell
cd dotnet/samples/04-hosting/FoundryHostedAgents/scripts
./New-ContributorStage.ps1 -Sample Hosted-ChatClientAgent
```

The script copies the sample to a temp folder, packs the local Agent Framework source into a
`local-feed` folder next to it, and writes the `nuget.config` and `local-feed.props` that point the
build at those packages. All three travel inside the ZIP, so the server-side restore resolves the
framework from the upload. It prints the same `azd` commands as above, with `-m` pointing at the
staged copy.

For the full hosted-agent deployment guide, see the [official source-code deployment doc](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/deploy-hosted-agent-code).
