# Using Web Search with AI Agents

This sample demonstrates how to use the web search tool with AI agents. The web search tool allows agents to search the web for current information to answer questions accurately.

## What this sample demonstrates

- Creating agents with web search capabilities
- Using HostedWebSearchTool (MEAI abstraction)
- Using native SDK web search tools (ResponseTool.CreateWebSearchTool)
- Extracting text responses and URL citations from agent responses
- Managing agent lifecycle (creation and deletion)

## Prerequisites

Before you begin, ensure you have the following prerequisites:

- .NET 10 SDK or later
- Azure Foundry service endpoint and deployment configured
- Bing Grounding connection configured in your Azure Foundry project
- Azure CLI installed and authenticated (for Azure credential authentication)

**Note**: This demo uses Azure CLI credentials for authentication. Make sure you're logged in with `az login` and have access to the Azure Foundry resource. For more information, see the [Azure CLI documentation](https://learn.microsoft.com/cli/azure/authenticate-azure-cli-interactively).

Set the following environment variables:

```powershell
$env:AZURE_FOUNDRY_PROJECT_ENDPOINT="https://your-foundry-service.services.ai.azure.com/api/projects/your-foundry-project" # Replace with your Azure Foundry resource endpoint
$env:AZURE_FOUNDRY_PROJECT_DEPLOYMENT_NAME="gpt-4o-mini"  # Optional, defaults to gpt-4o-mini
$env:AZURE_FOUNDRY_BING_CONNECTION_ID="your-bing-connection-id"  # Your Bing Grounding connection ID
```

## Run the sample

Navigate to the FoundryAgents sample directory and run:

```powershell
cd dotnet/samples/GettingStarted/FoundryAgents
dotnet run --project .\FoundryAgents_Step25_WebSearch
```

## Expected behavior

The sample will:

1. Create two agents with web search capabilities:
   - Option 1: Using HostedWebSearchTool (MEAI abstraction)
   - Option 2: Using native SDK web search tools
2. Run the agent with a query: "What's the weather today in Seattle?"
3. The agent will use the web search tool to find current information
4. Display the text response from the agent
5. Display any URL citations from web search results
6. Clean up resources by deleting both agents
