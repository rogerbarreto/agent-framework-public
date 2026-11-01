# Browser Automation via a Foundry Toolbox

This sample shows how to use the Browser Automation tool through a Foundry Toolbox. The sample creates a toolbox that contains the Browser Automation tool, points an `McpClient` at the toolbox's MCP endpoint, and passes the discovered tools to the agent. The tool drives a cloud browser hosted by a [Microsoft Playwright Workspace](https://learn.microsoft.com/azure/app-testing/playwright-workspaces/overview-what-is-microsoft-playwright-workspaces).

## What this sample demonstrates

- Looking up a Browser Automation project connection by name with `AIProjectClient.Connections`
- Creating (or replacing) a toolbox version with a `BrowserAutomationToolboxTool`
- Connecting to the toolbox's MCP endpoint via Streamable HTTP transport
- Injecting a fresh Azure AI bearer token (`https://ai.azure.com/.default`) on every MCP request
- Passing the discovered MCP tools to `AIProjectClient.AsAIAgent(...)`

## Prerequisites

- .NET 10 SDK or later
- A Microsoft Foundry project and model deployment
- An authenticated Azure identity (for example, sign in with `az login`)
- A Microsoft Playwright Workspace
- A Browser Automation project connection to that Playwright Workspace in your Microsoft Foundry project

The Browser Automation tool is available in the following regions: `australiaeast`, `eastasia`, `eastus`, `japaneast`, `switzerlandnorth`, `westeurope`, and `westus3`.

### Set up the Browser Automation connection

Follow [Set up Browser Automation in Microsoft Foundry](https://learn.microsoft.com/azure/foundry/agents/how-to/tools/browser-automation#set-up-browser-automation) to connect your Playwright Workspace, and use the connection name you entered as the value of `BROWSER_AUTOMATION_CONNECTION_NAME`. The sample looks up the connection and passes its ID to the toolbox tool.

Prefer Microsoft Entra ID authentication for the connection when supported. If you use a Playwright Workspace access token, treat it like a password and never commit it to source control or include it in prompts or logs.

Set the following environment variables:

```powershell
$env:FOUNDRY_PROJECT_ENDPOINT="https://your-foundry-service.services.ai.azure.com/api/projects/your-foundry-project"
$env:FOUNDRY_MODEL="gpt-5.4-mini"
$env:BROWSER_AUTOMATION_CONNECTION_NAME="your-browser-automation-connection-name"
```

The sample creates a toolbox named `browser_automation_toolbox` in your Foundry project on
startup, then connects to its MCP endpoint at
`{FOUNDRY_PROJECT_ENDPOINT}/toolboxes/browser_automation_toolbox/mcp?api-version=v{version}`.

## Run the sample

```powershell
dotnet run
```

Browser automation tasks can take a few minutes to complete, so the sample configures five-minute timeouts.

> [!WARNING]
> The agent's response can include a Live View URL that grants access to the browser session. Treat it as sensitive: only open it in a trusted environment, and redact it before sharing output or saving logs.
