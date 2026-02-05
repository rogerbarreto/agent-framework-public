# Using Browser Automation with AI Agents

This sample demonstrates how to use the Browser Automation tool with AI agents. The Browser Automation tool allows agents to perform web browsing tasks, navigate websites, and extract information from web pages.

## What this sample demonstrates

- Creating agents with Browser Automation capabilities
- Using PromptAgentDefinition with AgentTool.CreateBrowserAutomationTool
- Querying the agent to perform browser automation tasks
- Managing agent lifecycle (creation and deletion)

## Prerequisites

Before you begin, ensure you have the following prerequisites:

- .NET 10 SDK or later
- Azure Foundry service endpoint and deployment configured
- Browser Automation connection configured in your Azure Foundry project
- Azure CLI installed and authenticated (for Azure credential authentication)

**Note**: This demo uses Azure CLI credentials for authentication. Make sure you're logged in with `az login` and have access to the Azure Foundry resource. For more information, see the [Azure CLI documentation](https://learn.microsoft.com/cli/azure/authenticate-azure-cli-interactively).

Set the following environment variables:

```powershell
$env:AZURE_FOUNDRY_PROJECT_ENDPOINT="https://your-foundry-service.services.ai.azure.com/api/projects/your-foundry-project" # Replace with your Azure Foundry resource endpoint
$env:AZURE_FOUNDRY_PROJECT_DEPLOYMENT_NAME="gpt-4o-mini"  # Optional, defaults to gpt-4o-mini
$env:BROWSER_AUTOMATION_PROJECT_CONNECTION_ID="your-browser-automation-connection-id" # Replace with your Browser Automation connection ID
```

## Run the sample

Navigate to the FoundryAgents sample directory and run:

```powershell
cd dotnet/samples/GettingStarted/FoundryAgents
dotnet run --project .\FoundryAgents_Step24_BrowserAutomation
```

## Expected behavior

The sample will:

1. Create an agent with Browser Automation capabilities using the native SDK approach
2. Send a query to the agent asking it to:
   - Navigate to finance.yahoo.com
   - Search for Microsoft stock (MSFT)
   - Report the year-to-date percentage change
3. Display the agent's response with the stock information
4. Clean up resources by deleting the agent
