# Hosted-Toolbox-AuthPaths — REPL Client

Interactive REPL that drives the [`Hosted-Toolbox-AuthPaths`](../../Hosted-Toolbox-AuthPaths/) hosted agent across all four authentication paths, with first-class handling of the **OAuth consent** flow for path #4.

## What this sample teaches

| Aspect | This client |
|---|---|
| Transport | Raw `HttpClient` + Server-Sent Events parsing |
| Auth to Foundry | `DefaultAzureCredential` exchanged for `https://ai.azure.com/.default` |
| Streaming | `response.output_text.delta` events printed inline |
| OAuth consent | Detects `response.output_item.added` events whose item type is `oauth_consent_request`, opens the consent URL in the browser, waits for the user, then retries the prompt with `previous_response_id` |
| Conversation state | `previous_response_id` is threaded turn over turn; `/new` resets |

The client deliberately bypasses the .NET OpenAI SDK because OpenAI 2.9.1 does not understand the `oauth_consent_request` item type and falls back to an internal unknown-item resource that callers cannot inspect. Going to the wire keeps the sample faithful to the Python reference contract emitted by the Foundry hosting bridge.

## How OAuth consent flows

```
You> @me — who am I on GitHub?

(client sends POST .../responses, streams SSE)

[server SSE]
event: response.output_item.added
data: {
  "type": "response.output_item.added",
  "item": {
    "type": "oauth_consent_request",
    "id": "oacr_<hex>",
    "consent_link": "https://login.example.com/oauth/...",
    "server_label": "auth-paths-oauth-toolbox"
  }
}
event: response.incomplete
data: { "response": { "id": "resp_..." } }

(client opens browser at consent_link, prints URL, waits for Enter)

You press Enter once you've completed the OAuth grant in the browser.

(client re-sends the same prompt with `previous_response_id = resp_...`)
```

If consent has been completed, the retry runs the tool and streams normal text. If the operator has not yet authorized the connection (path #4 prerequisite — see the server README's troubleshooting table), the retry will surface the same consent item again.

## Prerequisites

1. Deploy the [`Hosted-Toolbox-AuthPaths`](../../Hosted-Toolbox-AuthPaths/) agent and ensure the four toolbox connections are configured per its README.
2. `az login` with an identity that holds **Azure AI User** on the project (and that exists in the same tenant as the project — cross-tenant exchange is not supported for path #4).

## Environment

| Variable | Required | Default | Notes |
|---|---|---|---|
| `AZURE_AI_PROJECT_ENDPOINT` | yes | — | e.g. `https://<host>/api/projects/<project>` |
| `AZURE_AI_AGENT_NAME` | no | `hosted-toolbox-auth-paths-agent` | Server-side agent name registered during deployment |
| `AZURE_AI_MODEL` | no | `gpt-4o` | Advertised in the request body. The hosted agent uses its own server-side deployment, so the value is informational. |

A `.env` file in this folder is picked up automatically by `DotNetEnv` if present.

## Run

```powershell
cd dotnet/samples/04-hosting/FoundryHostedAgents/responses/Using-Samples/Hosted-Toolbox-AuthPaths-Client
dotnet run
```

REPL commands:
- `quit` — exit
- `/new` — drop `previous_response_id` and start a fresh conversation

## Suggested prompts (one per auth path)

| Path | Prompt | What it exercises |
|---|---|---|
| 1 (API key) | `Use the github MCP tool to list my recent PRs.` | Toolbox PAT injection |
| 2 (Entra agent identity) | `Detect the language of "Bonjour le monde".` | Foundry mints an Entra token for the agent's instance identity |
| 4 (OAuth user identity) | `@me — who am I on GitHub?` | Triggers the consent flow demonstrated above the first time |
| 5 (inline bearer — anti-pattern) | `Search the azure-rest-api-specs repo for "preview" specs.` | Literal bearer on the tool entry |

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `HTTP 401 ... InvalidAuthenticationToken` | `az login` not run, or the signed-in identity lacks Azure AI User on the project. |
| The consent URL keeps reappearing after browser sign-in | The operator has not authorized the connection in **Foundry portal → Connections → Edit authentication → Authorize**, or your tenant differs from the project's tenant. |
| No text streams back at all but no error either | The agent answered with a tool-only response (e.g. fetched data and stopped). Ask a question that requires text composition. |
| `HTTP 404` on first request | `AZURE_AI_AGENT_NAME` does not match a registered agent. List with `az rest --method GET --url "$endpoint/agents?api-version=v1"`. |
