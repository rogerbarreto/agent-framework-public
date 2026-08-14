# Hosted-Workflow-Resilient

A durable, long-running **workflow** hosted as a Foundry Hosted Agent using the **Responses protocol**. It is the same English to French to Spanish back to English translation chain as [`Hosted-Workflow-Simple`](../Hosted-Workflow-Simple/README.md), with one difference: it opts into **resilient background responses**. AgentServer re-invokes an interrupted background response, and the restored AgentSession lets the workflow runtime continue from its saved workflow checkpoint.

## What "resilient" means here

- **Long-running with no client connected.** When a caller starts a background response (`store: true`, `background: true`), the platform keeps the agent running even if the caller disconnects.
- **Crash recovery.** If the container crashes or is recycled mid-run, AgentServer restarts the response handler with `IsRecovery = true`. Foundry Hosting reloads the AgentSession, and the workflow runtime uses the checkpoint reference in that session to restore execution. Work after the saved checkpoint runs again.
- **Best-effort session snapshots.** The handler saves the AgentSession after completed Responses output items and again at normal turn completion. These saves are not workflow checkpoints and are not `ResponseEventStream.Checkpoint()` calls. If an incremental save fails or has not yet captured the newest workflow checkpoint, recovery can repeat additional work.
- **Stable executor ids.** Recovery matches the saved checkpoint to the rebuilt workflow by executor id, and an agent-backed step derives its id from the agent's id. A default agent gets a fresh random id per process, which would never match after a restart, so each agent is created with an explicit stable `Id`:

  ```csharp
  AIAgent frenchAgent = chatClient.AsAIAgent(options: new()
  {
      Id = "french-translator",
      Name = "french-translator",
      ChatOptions = new() { Instructions = "...translate to French." },
  });
  ```

- **Opt-in, off by default.** Turning on resilience is one line:

  ```csharp
  builder.Services.AddFoundryResponses(agent, configure: o => o.ResilientBackground = true);
  ```

  Durability applies only to background responses. A foreground response (the caller waits on the connection) is not durable: a crash simply fails it.

## What is persisted

| State | Owner | Purpose |
|---|---|---|
| Background task, response events, and selected response snapshots | AgentServer | Re-invoke the handler and let clients reconnect to the same response |
| AgentSession | `FoundryAgentSessionStore` | Restore agent state and the workflow checkpoint reference |
| Workflow checkpoints | `FoundryJsonCheckpointStore` | Restore workflow executors, queued messages, pending requests, and state |

`PersistedResponse` is the last `ResponseObject` snapshot saved by AgentServer. This hosting adapter
does not call `ResponseEventStream.Checkpoint()`, so an interrupted turn normally receives the
initial `response.created` snapshot. Workflow continuation comes from the checkpoint referenced by
the restored AgentSession, not from `PersistedResponse`.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- `az login` plus a Foundry **project endpoint** and a **model deployment** (each translation step calls the model).

## Configuration

```bash
cp .env.example .env
# set FOUNDRY_PROJECT_ENDPOINT and FOUNDRY_MODEL
```

## Run locally (contributors)

This project uses `ProjectReference` to build against the local Agent Framework source.

```bash
az login
export FOUNDRY_PROJECT_ENDPOINT=https://<account>.services.ai.azure.com/api/projects/<project>
export FOUNDRY_MODEL=gpt-4o

cd dotnet/samples/04-hosting/FoundryHostedAgents/responses/Hosted-Workflow-Resilient
dotnet run
```

The agent starts on `http://localhost:8088`.

### Local crash-and-recover walkthrough

Resilient recovery needs a state store that survives a process restart. Locally the SDK auto-selects a file-backed store when `FOUNDRY_HOSTING_ENVIRONMENT` is unset; pin the store root and the session id so a restart finds the in-progress response:

```bash
export AGENTSERVER_STATE_ROOT=$PWD/.agentserver-state
export FOUNDRY_AGENT_SESSION_ID=local-demo-session
dotnet run
```

1. Start a background response and stream it. Capture the response id (`"id":"caresp_..."`):

   ```bash
   curl -N -s http://localhost:8088/responses \
     -H 'content-type: application/json' \
     -d '{"input":"renewable energy supply chains","stream":true,"store":true,"background":true}'
   ```

2. After a translation step or two, stop the process (Ctrl+C, or kill it) to simulate a crash.

3. Restart against the **same** `AGENTSERVER_STATE_ROOT` and `FOUNDRY_AGENT_SESSION_ID`. On startup the resilient task scanner reclaims the in-progress response and re-invokes the handler. The handler reloads the AgentSession, then the workflow runtime restores the checkpoint referenced by that session.

4. Reconnect and watch it finish:

   ```bash
   curl -N -s "http://localhost:8088/responses/<response_id>?stream=true"
   ```

## How local mode works

| Env var | Effect |
|---|---|
| `FOUNDRY_HOSTING_ENVIRONMENT` (**unset**) | AgentServer uses its local file-backed task, response, and Foundry state-store implementations instead of hosted platform APIs. |
| `AGENTSERVER_STATE_ROOT` | Root for local AgentServer response and task records plus the local Foundry state-store fallback used by agent sessions and workflow checkpoints. It must survive the restart. |
| `FOUNDRY_AGENT_SESSION_ID` | The session pinned across restarts so recovery finds the in-progress response. |

## Deploy to Foundry

Initialize an `azd` project from this sample's manifest, then deploy:

```bash
mkdir hosted-workflow-resilient && cd hosted-workflow-resilient
azd ai agent init -m https://github.com/microsoft/agent-framework/blob/main/dotnet/samples/04-hosting/FoundryHostedAgents/responses/Hosted-Workflow-Resilient/agent.manifest.yaml
azd deploy
```

Drive it with a background response (`"background": true`), then exercise crash recovery by letting the platform restart the container. See the [official deployment guide](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/deploy-hosted-agent).
