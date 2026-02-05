# Using Azure AI Search with AI Agents

This sample demonstrates how to use Azure AI Search as a tool with AI agents to search indexed content and provide answers with citations.

## What this sample demonstrates

- Creating agents with Azure AI Search capabilities
- Configuring search indexes with connection IDs
- Extracting citations/annotations from search results
- Managing agent lifecycle (creation and deletion)

## Prerequisites

Before you begin, ensure you have the following prerequisites:

- .NET 10 SDK or later
- Azure Foundry service endpoint and deployment configured
- Azure AI Search resource with an indexed content
- Azure CLI installed and authenticated (for Azure credential authentication)

**Note**: This demo uses Azure CLI credentials for authentication. Make sure you're logged in with `az login` and have access to the Azure Foundry resource. For more information, see the [Azure CLI documentation](https://learn.microsoft.com/cli/azure/authenticate-azure-cli-interactively).

Set the following environment variables:

```powershell
$env:AZURE_FOUNDRY_PROJECT_ENDPOINT="https://your-foundry-service.services.ai.azure.com/api/projects/your-foundry-project"
$env:AZURE_FOUNDRY_PROJECT_DEPLOYMENT_NAME="gpt-4o-mini"  # Optional, defaults to gpt-4o-mini
$env:AI_SEARCH_PROJECT_CONNECTION_ID="your-search-connection-id"
$env:AI_SEARCH_INDEX_NAME="your-index-name"
```

## Run the sample

Navigate to the FoundryAgents sample directory and run:

```powershell
cd dotnet/samples/GettingStarted/FoundryAgents
dotnet run --project .\FoundryAgents_Step17_AzureAISearch
```

## Expected behavior

The sample will:

1. Create an agent with Azure AI Search tool configured with your search index
2. Run a query against the agent asking about the indexed content
3. Display the response with any citations from the search results
4. Clean up resources by deleting the agent
