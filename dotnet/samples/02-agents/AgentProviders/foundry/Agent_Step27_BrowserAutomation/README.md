# Browser Automation with the Responses API

This sample shows how to use the Browser Automation tool with a `ChatClientAgent` using the Responses API directly.
The tool lets the agent drive a cloud browser hosted by a [Microsoft Playwright Workspace](https://learn.microsoft.com/azure/app-testing/playwright-workspaces/overview-what-is-microsoft-playwright-workspaces) to complete tasks on real websites.

## What this sample demonstrates

- Looking up a Browser Automation project connection by name with `AIProjectClient.Connections`
- Configuring `BrowserAutomationToolOptions` with the connection ID
- Using `FoundryAITool.CreateBrowserAutomationTool()` with `ChatClientAgent`
- Requiring tool use with `ChatToolMode.RequireAny` and streaming the agent's response

## Prerequisites

- .NET 10 SDK or later
- Microsoft Foundry service endpoint and deployment configured
- An authenticated Azure identity (for example, sign in with `az login`)
- A Microsoft Playwright Workspace
- A Browser Automation project connection to that Playwright Workspace in your Microsoft Foundry project

The Browser Automation tool is available in the following regions: `australiaeast`, `eastasia`, `eastus`, `japaneast`, `switzerlandnorth`, `westeurope`, and `westus3`.

### Set up the Browser Automation connection

Follow [Set up Browser Automation in Microsoft Foundry](https://learn.microsoft.com/azure/foundry/agents/how-to/tools/browser-automation#set-up-browser-automation) to connect your Playwright Workspace:

1. Open your project in the Microsoft Foundry portal and select **Build** > **Tools**.
2. Add the **Browser Automation** tool, enter a connection name, select your Playwright Workspace, and choose the authentication method.
3. Use the connection name you entered as the value of `BROWSER_AUTOMATION_CONNECTION_NAME`. The sample looks up the connection and passes its ID to the tool.

Prefer Microsoft Entra ID authentication for the connection when supported. If you use a Playwright Workspace access token, treat it like a password and never commit it to source control or include it in prompts or logs.

Set the following environment variables:

```powershell
$env:FOUNDRY_PROJECT_ENDPOINT="https://your-foundry-service.services.ai.azure.com/api/projects/your-foundry-project"
$env:FOUNDRY_MODEL="gpt-5.4-mini"
$env:BROWSER_AUTOMATION_CONNECTION_NAME="your-browser-automation-connection-name"
```

## Run the sample

```powershell
dotnet run
```

Browser automation tasks can take a few minutes to complete, so the sample configures a five-minute network timeout.

> [!WARNING]
> The agent's response can include a Live View URL that grants access to the browser session. Treat it as sensitive: only open it in a trusted environment, and redact it before sharing output or saving logs.
