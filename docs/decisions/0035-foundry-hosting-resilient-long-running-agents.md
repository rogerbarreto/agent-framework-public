---
status: proposed
contact: rogerbarreto
date: 2026-08-21
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
This forwarding must happen in the callback passed directly to `AddResponsesServer`. The SDK makes
two process-level choices during that registration call: whether local SSE replay uses durable
storage and whether the conversation task accepts steering. Configuring the options only through
the later `IOptions` pipeline is too late for those choices.

The first `AddFoundryResponses` call owns this host-level configuration. Repeated calls do not
register another Responses server or redefine its resilience mode.

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
   incomplete. The AgentServer shutdown token is linked to the token passed into the MAF agent so
   long-running model, tool, and workflow operations stop promptly. The handler also checks
   `IsShutdownRequested` after each agent update, because an agent may consume cancellation and
   return normally instead of throwing.
4. Best-effort save the agent session after each `ResponseOutputItemDoneEvent`, with an
   authoritative end-of-turn save in `finally` (skipped when the turn failed). These incremental
   saves are neither workflow checkpoints nor AgentServer response-stream checkpoints.

### Handler contract on steering

When a second input arrives for an active steerable conversation:

1. AgentServer returns a response with `status=queued`, records the input, increments
   `PendingInputCount` on the active handler context, and signals that handler's cancellation token.
2. The superseded handler invocation has `IsSteeredTurn=false`. If a cancellation-aware MAF
   operation throws `OperationCanceledException`, Foundry Hosting uses `PendingInputCount > 0` to
   distinguish steering from shutdown and client cancellation.
3. Foundry Hosting completes the superseded response cleanly and saves its `AgentSession` with a
   non-cancelled save token. This gives the queued turn the latest committed MAF state.
4. AgentServer invokes the handler again with `IsSteeredTurn=true`. This is not crash recovery:
   `IsRecovery=false`, so the new input is converted to MAF messages normally. The same
   `conversation_id` resolves the same persisted `AgentSession`.

No special MAF branch is required merely because `IsSteeredTurn=true`. The classification is
available for handlers that need different application behavior; the generic adapter treats the
drained input as the next normal turn on the same session.

Skipping `ResponseEventStream.Checkpoint()` on steering does not mean discarding everything the
superseded response produced. The response reaches a terminal `completed` event, which persists its
terminal representation. Separately, the `AgentSession` save preserves upstream MAF state. For a
workflow, `LastCheckpoint` advances only after a completed superstep, so a session saved after
steering still points at the last complete workflow boundary rather than the interrupted
superstep.

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
- Handler-level tests cover recovery input skip, consumption of an available response snapshot,
  and mid-stream session-save failure.
- A local two-lifetime integration test starts a real Responses host, persists a MAF
  `AgentSession`, stops the host, starts a new host over the same local AgentServer state, and
  verifies that the same response completes without re-injecting the original input.
- A local steering integration test sends two real HTTP turns through AgentServer and the MAF
  adapter. It verifies `queued`, serial execution, delivery of the steering input, and reuse of the
  persisted session.
- Live Foundry tests cover background continuation without client traffic, hard process
  termination through `Environment.Exit`, recovery in a different process incarnation, transient
  `404`/`424` polling responses during replacement, and long-running steering on the same
  conversation.
- The checkpoint-index optimistic-concurrency retry count is configurable through
  `FoundryJsonCheckpointStore`, with a default of eight attempts.
- Package floor: Azure.AI.AgentServer Core beta.28, Invocations beta.6, Responses beta.8.
