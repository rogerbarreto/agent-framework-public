# What this sample demonstrates

This sample demonstrates how to use Foundry tools with an AI agent via the `UseFoundryTools` extension. The agent is configured with two tool types: an MCP (Model Context Protocol) connection for fetching Microsoft Learn documentation and a code interpreter for running code when needed.

Key features:

- Configuring Foundry tools using `UseFoundryTools` with MCP and code interpreter
- Connecting to an external MCP tool via a Foundry project connection
- Using `AzureCliCredential` for Azure authentication
- OpenTelemetry instrumentation for both the chat client and agent

## Prerequisites

Before running this sample, ensure you have:

1. **.NET 10 SDK** or later installed
2. **Azure CLI** installed and authenticated (`az login`)
3. An **Azure AI Foundry project** with a chat model deployed (e.g., `gpt-5.2`, `gpt-4o-mini`)
4. The **Azure AI Developer** role assigned on the Foundry resource (see [Role Assignment](#step-3-assign-the-azure-ai-developer-role) below)
5. An **MCP tool connection** configured in your Foundry project (see [MCP Tool Setup](#step-2-create-the-mcp-tool-connection) below)

## Setup Guide

### Step 1: Authenticate with Azure CLI

Make sure you're logged in with the account that has access to your Azure AI Foundry project:

```powershell
az login
az account show  # Verify the correct subscription is selected
```

### Step 2: Create the MCP Tool Connection

The agent uses a Foundry MCP tool connection to access Microsoft Learn documentation tools. You need to create this connection in your Azure AI Foundry project.

1. Go to the [Azure AI Foundry portal](https://ai.azure.com)
2. Navigate to your project
3. Go to **Connected resources** → **+ New connection** → **Model Context Protocol tool**
4. Fill in the following:
   - **Name**: `SampleMCPTool` (or any name you prefer)
   - **Remote MCP Server endpoint**: `https://learn.microsoft.com/api/mcp`
   - **Authentication**: `Unauthenticated`
5. Click **Connect**

The connection **name** you chose (e.g., `SampleMCPTool`) is the value you'll use for `MCP_TOOL_CONNECTION_ID`.

### Step 3: Assign the Azure AI Developer Role

The `UseFoundryTools` extension requires the `Microsoft.CognitiveServices/accounts/AIServices/agents/write` data action to resolve and invoke MCP tools. This is included in the **Azure AI Developer** role.

Even if you created the Foundry project, you may not have this role by default. To assign it:

```powershell
# Replace with your user email and resource path
az role assignment create `
  --role "Azure AI Developer" `
  --assignee "your-email@microsoft.com" `
  --scope "/subscriptions/{subscription-id}/resourceGroups/{resource-group}/providers/Microsoft.CognitiveServices/accounts/{account-name}"
```

> **Note**: You need **Owner** or **User Access Administrator** permissions on the resource to assign roles. If you don't have this, you may need to request JIT (Just-In-Time) elevated access via [Azure PIM](https://portal.azure.com/#view/Microsoft_Azure_PIMCommon/ActivationMenuBlade/~/aadmigratedresource) first.

For more details on permissions, see [Azure AI Foundry Permissions](https://aka.ms/FoundryPermissions).

### Step 4: Set Environment Variables

The sample requires `AZURE_OPENAI_ENDPOINT` and `AZURE_AI_PROJECT_ENDPOINT`. The `UseFoundryTools` extension internally uses `AZURE_AI_PROJECT_ENDPOINT` to resolve tool connections.

```powershell
# Your Azure OpenAI endpoint
$env:AZURE_OPENAI_ENDPOINT="https://your-openai-resource.openai.azure.com/"

# Your Azure AI Foundry project endpoint (required by UseFoundryTools)
$env:AZURE_AI_PROJECT_ENDPOINT="https://your-resource.services.ai.azure.com/api/projects/your-project"

# Chat model deployment name (defaults to gpt-4o-mini if not set)
$env:AZURE_OPENAI_DEPLOYMENT_NAME="gpt-5.2"

# The MCP tool connection name (just the name, not the full ARM resource ID)
$env:MCP_TOOL_CONNECTION_ID="SampleMCPTool"
```

> **Important**: `MCP_TOOL_CONNECTION_ID` should be the connection **name** only (e.g., `SampleMCPTool`), not the full ARM resource path.

## Running the Sample

```powershell
dotnet run
```

This starts the hosted agent locally on `http://localhost:8088/`.

### Interacting with the Agent

You can use the `run-requests.http` file in this directory, or send requests directly:

```powershell
$body = @{ input = "Search for Azure AI Agent Service documentation" } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:8088/responses" -Method Post -Body $body -ContentType "application/json"
```

## How It Works

1. An `AzureOpenAIClient` is created with `AzureCliCredential` and used to get a chat client
2. The chat client is wrapped with `UseFoundryTools` which registers two Foundry tool types:
   - **MCP connection**: Connects to an external MCP server (Microsoft Learn) via the project connection name, providing documentation fetch and search capabilities
   - **Code interpreter**: Allows the agent to execute code snippets when needed
3. `UseFoundryTools` resolves the connection using `AZURE_AI_PROJECT_ENDPOINT` internally
4. A `ChatClientAgent` is created with instructions guiding it to use the MCP tools for documentation queries
5. The agent is hosted using `RunAIAgentAsync` which exposes the OpenAI Responses-compatible API endpoint

## Troubleshooting

### `PermissionDenied` — lacks `agents/write` data action

Assign the **Azure AI Developer** role to your user on the Cognitive Services resource. See [Step 3](#step-3-assign-the-azure-ai-developer-role).

### `Project connection ... was not found`

Make sure `MCP_TOOL_CONNECTION_ID` contains only the connection **name** (e.g., `SampleMCPTool`), not the full ARM resource ID path.

### `AZURE_AI_PROJECT_ENDPOINT must be set`

The `UseFoundryTools` extension requires `AZURE_AI_PROJECT_ENDPOINT` to be set, even though `Program.cs` reads `AZURE_OPENAI_ENDPOINT`. Both must be configured. See [Step 4](#step-4-set-environment-variables).
