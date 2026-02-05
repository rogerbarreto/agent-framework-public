# Using Content Filtering (RAI) with AI Agents

This sample demonstrates how to use content filtering with AI agents using Azure AI Foundry's Responsible AI (RAI) policies.

## What this sample demonstrates

- Creating agents with content filtering enabled via `ContentFilterConfiguration`
- Configuring RAI policies for content moderation
- Handling potential content filter responses

## Prerequisites

Before you begin, ensure you have the following prerequisites:

- .NET 10 SDK or later
- Azure Foundry service endpoint and deployment configured
- Azure CLI installed and authenticated (for Azure credential authentication)
- An RAI policy created in Azure AI Foundry

### Creating an RAI Policy

1. Go to [Azure AI Foundry](https://ai.azure.com) and sign in
2. Navigate to your project
3. Go to **Guardrails + Controls** > **Content Filters**
4. Create a new content filter or use an existing one
5. Configure the filter categories and severity levels as needed
6. Note the policy resource ID (format: `/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.CognitiveServices/accounts/{accountName}/raiPolicies/{policyName}`)

**Note**: This demo uses Azure CLI credentials for authentication. Make sure you're logged in with `az login` and have access to the Azure Foundry resource. For more information, see the [Azure CLI documentation](https://learn.microsoft.com/cli/azure/authenticate-azure-cli-interactively).

## Environment Variables

Set the following environment variables:

```powershell
$env:AZURE_FOUNDRY_PROJECT_ENDPOINT="https://your-foundry-service.services.ai.azure.com/api/projects/your-foundry-project"
$env:AZURE_FOUNDRY_PROJECT_DEPLOYMENT_NAME="gpt-4o-mini"  # Optional, defaults to gpt-4o-mini
$env:RAI_POLICY_NAME="/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.CognitiveServices/accounts/{accountName}/raiPolicies/{policyName}"
```

## Run the sample

Navigate to the FoundryAgents sample directory and run:

```powershell
cd dotnet/samples/GettingStarted/FoundryAgents
dotnet run --project .\FoundryAgents_Step20_ContentFiltering
```

## Expected behavior

The sample will:

1. Create an agent with content filtering enabled using your RAI policy
2. Run test queries through the agent
3. The agent applies content filtering based on your RAI policy configuration
4. If content is blocked by the filter, an exception will be thrown
5. Clean up resources by deleting the agent

## Content Filter Configuration

The `ContentFilterConfiguration` class accepts a `PolicyName` parameter that specifies the full Azure resource ID of your RAI policy:

```csharp
ContentFilterConfiguration contentFilterConfig = new(raiPolicyName);

PromptAgentDefinition agentDefinition = new(model: deploymentName)
{
    Instructions = "You are a helpful assistant.",
    ContentFilterConfiguration = contentFilterConfig
};
```

## Learn More

- [Azure AI Content Safety](https://learn.microsoft.com/azure/ai-services/content-safety/)
- [Responsible AI practices](https://www.microsoft.com/ai/responsible-ai)
