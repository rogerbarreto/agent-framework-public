# Using Microsoft Fabric Tool with AI Agents

This sample demonstrates how to use the Microsoft Fabric tool with AI Agents, allowing agents to query and interact with data in Microsoft Fabric workspaces.

## What this sample demonstrates

- Creating agents with Microsoft Fabric data access capabilities
- Using FabricDataAgentToolOptions to configure Fabric connections
- Using native SDK fabric tools (AgentTool.CreateMicrosoftFabricTool)
- Managing agent lifecycle (creation and deletion)

## Prerequisites

Before you begin, ensure you have the following prerequisites:

- .NET 10 SDK or later
- Azure Foundry service endpoint and deployment configured
- Azure CLI installed and authenticated (for Azure credential authentication)
- A Microsoft Fabric workspace with a configured project connection in Azure Foundry

**Note**: This demo uses Azure CLI credentials for authentication. Make sure you're logged in with `az login` and have access to the Azure Foundry resource. For more information, see the [Azure CLI documentation](https://learn.microsoft.com/cli/azure/authenticate-azure-cli-interactively).

Set the following environment variables:

```powershell
$env:AZURE_FOUNDRY_PROJECT_ENDPOINT="https://your-foundry-service.services.ai.azure.com/api/projects/your-foundry-project" # Replace with your Azure Foundry resource endpoint
$env:AZURE_FOUNDRY_PROJECT_DEPLOYMENT_NAME="gpt-4o-mini"  # Optional, defaults to gpt-4o-mini
$env:FABRIC_PROJECT_CONNECTION_ID="your-fabric-connection-id"  # The Fabric project connection ID from Azure Foundry
```

## Run the sample

Navigate to the FoundryAgents sample directory and run:

```powershell
cd dotnet/samples/GettingStarted/FoundryAgents
dotnet run --project .\FoundryAgents_Step23_MicrosoftFabric
```

## Expected behavior

The sample will:

1. Create an agent with Microsoft Fabric tool capabilities
2. Configure the agent with a Fabric project connection
3. Run the agent with a query about available Fabric data
4. Display the agent's response
5. Clean up resources by deleting the agent
