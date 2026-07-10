# Agent Framework hosting helper samples

End-to-end samples for exposing Agent Framework targets through app-owned hosting routes.

The helper-first hosting packages provide protocol conversion and optional execution state. The
application still owns the web framework, native SDK clients, authentication, response construction, and
deployment shape.

| Sample | What it shows |
|---|---|
| [`local_responses/`](./local_responses) | One agent behind an app-owned ASP.NET Core route using the `OpenAIResponses` helper functions plus `HostedAgentState` / `AgentSessionStore`. Start here to learn the helper seam. |
| [`local_responses_workflow/`](./local_responses_workflow) | A workflow target behind an app-owned ASP.NET Core route using the `OpenAIResponses` helper functions, `HostedWorkflowState`, an explicit `CheckpointManager`, and an app-owned checkpoint cursor. |

Each sample is a **client/server pair**. Unlike the Python samples (which run a server `app.py` and calling
scripts as loose files), .NET samples are projects, so each pair is split into two projects:

```
local_responses/
├── Server/   # exposes POST /responses using the OpenAIResponses helpers
└── Client/   # consumes it two ways: CC (IChatClient) and MAF (AIAgent)
```

The `Client` shows the two idiomatic ways to consume the endpoint from .NET, both against the same server:

- **CC** — a plain `Microsoft.Extensions.AI.IChatClient` (the lower-level chat-client path).
- **MAF** — a Microsoft Agent Framework `AIAgent` (the higher-level agent path).

## Relationship to `../foundry-hosted-agents/`

The sibling [`../FoundryHostedAgents/`](../FoundryHostedAgents) directory contains samples for agents that
run inside the Foundry Hosted Agents platform. Those samples use the Foundry-managed protocol surface with
no app-owned hosting route involved.

| Aspect | `af-hosting/` (this directory) | `FoundryHostedAgents/` |
|---|---|---|
| Server stack | App-owned ASP.NET Core + hosting protocol helpers | Foundry Hosted Agents runtime |
| Protocol surface | The app exposes the route and calls helpers | The platform exposes Responses + Invocations |
| When to pick this | You need custom hosting code or want to learn the helper seam | You want the Foundry-managed hosting surface |
