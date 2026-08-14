---
status: proposed
contact: rogerbarreto
date: 2026-08-14
deciders: Roger Barreto, Ben Thomas
consulted: Tao Chen, Ravi Teja Pidaparthi, Glenn Condron
informed: Agent Framework .NET team
---

# Resilient long-running agents in Microsoft.Agents.AI.Foundry.Hosting

## Context and Problem Statement

The Foundry Hosted Agents platform can run a hosted agent as a long job that continues when no
client is connected, and that the platform restarts after the container crashes or is recycled.
On restart the platform re-invokes the handler with the same input, sets `ResponseContext.IsRecovery`
to true, and supplies the last durable `ResponseObject` snapshot as `PersistedResponse`. The
snapshot is not the workflow checkpoint. Without an explicit `ResponseEventStream.Checkpoint()`,
it is normally the initial `response.created` snapshot and may contain no completed output items.

This applies only to **background** requests (`store=true`, `background=true`). Foreground requests
have no crash-recovery contract.

Python already exposes this through `resilient_background` and optional `steerable_conversations`.
.NET hosting must offer the same opt-in surface on top of the durable session and checkpoint storage
introduced for Foundry state stores (PR #7649).

## Decision Drivers

- Match the Python recovery contract.
- Opt-in and off by default; non-resilient hosts pay nothing.
- Prefer workflows: they already checkpoint between supersteps.
- Keep a lean API on `FoundryResponsesOptions`, forwarded to `ResponsesServerOptions`.
- Persist agent sessions through the Foundry state store (or its local fallback), not a second disk layout.

## Decision Outcome

Chosen option: **turn resilience on through the existing handler and registration path**.

### Public surface

`FoundryResponsesOptions.ResilientBackground` and `FoundryResponsesOptions.SteerableConversations`
are forwarded to `ResponsesServerOptions` so the AgentServer SDK enables recovery and steering.

```csharp
builder.Services.AddFoundryResponses(agent, configure: o => o.ResilientBackground = true);
```

### Handler contract on recovery

When `IsRecovery` is true:

1. Seed `ResponseEventStream` from the `PersistedResponse` that AgentServer provides. This preserves
   its response fields and any output watermark it carries. It does not select the workflow resume
   point.
2. Do not re-inject the original input or platform history. The restored `AgentSession` owns
   re-entry. For a workflow agent, the session contains the `LastCheckpoint` reference used by the
   workflow runtime. A regular agent has no equivalent within-turn workflow checkpoint, so recovery
   is best-effort and depends on its serialized session state.
3. On graceful shutdown of a resilient turn, call `ExitForRecoveryAsync` instead of emitting
   incomplete.
4. Best-effort save the agent session after each `ResponseOutputItemDoneEvent`, with an
   authoritative end-of-turn save in `finally` (skipped when the turn failed). These incremental
   saves are neither workflow checkpoints nor AgentServer response-stream checkpoints.

### State ownership

| State | Owner | Recovery purpose |
|---|---|---|
| Resilient task, SSE events, `ResponseObject` snapshots | AgentServer | Re-invoke the handler and reconnect clients to the same response |
| Serialized `AgentSession` | Foundry Hosting | Restore agent-owned state and the workflow checkpoint reference |
| Workflow execution checkpoints | Workflow runtime through `FoundryJsonCheckpointStore` | Restore executors, queued messages, pending requests, and workflow state |

The handler does not call `ResponseEventStream.Checkpoint()`. Therefore `PersistedResponse` must
not be interpreted as a workflow progress cursor or assumed to contain every output emitted before
the crash. AgentServer persists SSE events separately from selected `ResponseObject` snapshots.

### Relationship to durable storage (PR #7649)

Sessions and workflow checkpoints already go through `FoundryAgentSessionStore` /
`FoundryJsonCheckpointStore`. AgentServer separately owns resilient task records, response snapshots,
and SSE event replay. Resilience does not invent another store; it coordinates handler re-entry with
the existing session and workflow stores.

## Consequences

- Samples: `Hosted-Workflow-Resilient` and `Hosted-Steering`.
- Unit tests cover recovery input skip, consumption of an available response snapshot, and
  mid-stream session-save failure.
- Package floor: Azure.AI.AgentServer Core beta.29, Invocations beta.8, Responses beta.9 (local
  preview feed until nuget.org ships them).
