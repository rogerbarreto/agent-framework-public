# Invocations channel

Mounts an `AIAgent` on the JSON Invocations channel from `Microsoft.Agents.AI.Hosting.Channels`. The smallest demonstration of `AddAgentFrameworkHost` + a single `AddInvocationsChannel` + `MapAgentFrameworkHost`.

## What it shows

* `IHostApplicationBuilder.AddAgentFrameworkHost(agent)` → `AddInvocationsChannel()`
* `IEndpointRouteBuilder.MapAgentFrameworkHost()` mounts every channel rooted at its `Path`
* `POST /invocations/invoke` runs synchronously and returns the agent text
* `POST /invocations/invoke` with `background: true` returns a continuation token
* `GET /invocations/{continuationToken}` polls the background run

## Requirements

* `AZURE_OPENAI_ENDPOINT` set, `az login` completed (DefaultAzureCredential)
* `AZURE_OPENAI_DEPLOYMENT_NAME` optional; defaults to `gpt-5.4-mini`

## Try it

```bash
cd dotnet/samples/04-hosting/HostingChannels/01_Invocations
dotnet run
```

Sync run:

```bash
curl -X POST http://localhost:5000/invocations/invoke \
  -H "Content-Type: application/json" \
  -d '{ "input": "Tell me a short joke." }'
```

Background run + polling:

```bash
TOKEN=$(curl -s -X POST http://localhost:5000/invocations/invoke \
  -H "Content-Type: application/json" \
  -d '{ "input": "Outline a recipe for chocolate chip cookies.", "background": true }' \
  | jq -r .continuation_token)

curl http://localhost:5000/invocations/$TOKEN
```