# Hosted-Files

A hosted agent that exposes a small set of files baked into its container image as knowledge accessible through local C# function tools. Demonstrates the typical "data shipped with the agent" pattern for Foundry hosted agents.

The contents of [`resources/`](./resources/) are copied into the published output (see `HostedFiles.csproj`) and live at `/app/resources/` inside the container. The agent's two tools surface them to the model on demand:

| Tool | Description |
|------|-------------|
| `ListFiles` | Returns the names of files available to the agent. |
| `ReadFile` | Reads the full text contents of a file by name. |

Companion sample: [`Using-Samples/SessionFilesClient`](../Using-Samples/SessionFilesClient/) — a thin chat REPL (same shape as [`SimpleAgent`](../Using-Samples/SimpleAgent/)) that points at the deployed Hosted-Files endpoint via `FoundryAgent` and lets you ask questions whose answers come from the bundled files.

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

## Try it from the SessionFilesClient REPL

```bash
cd ../Using-Samples/SessionFilesClient
$env:AGENT_ENDPOINT = "http://localhost:8088"
$env:AGENT_NAME = "hosted-files"
dotnet run

You> Give me the total revenue in the contoso file.
Agent> The contoso file reports total revenue of "$1,482.6M".
```

The agent's `ListFiles`/`ReadFile` tools resolve relative paths against `/app/resources/`, find `contoso_q1_2026_report.txt`, and surface the figure verbatim.

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

The bundled `resources/` folder is part of the published output and ships inside the image.

## NuGet package users

If consuming the Agent Framework as a NuGet package, use the standard `Dockerfile` instead of `Dockerfile.contributor` and switch the `ProjectReference` entries in `HostedFiles.csproj` to `PackageReference` (commented section in the csproj).

## Adding more files

Drop additional text files into [`resources/`](./resources/). The csproj `<Content Include="resources\**\*" CopyToOutputDirectory="PreserveNewest" />` rule picks them up on the next `dotnet build` / `docker build`.

