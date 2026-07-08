---
status: accepted
contact: rogerbarreto
date: 2026-07-08
deciders: rogerbarreto
consulted: eavanvalkenburg
informed: []
---

# .NET hosting: OpenAI Responses protocol helpers for app-owned routing

Realizes the helper-first direction of [ADR-0027](0027-hosting-channels.md) for .NET.

## Context and Problem Statement

[ADR-0027](0027-hosting-channels.md) refocused the (Python) hosting design away from a channel
framework toward **protocol conversion helpers plus optional execution state**: Agent Framework owns
protocol-native <-> run conversion, while the application owns HTTP routing, authentication,
middleware, storage, and native SDK calls.

.NET already ships `Microsoft.Agents.AI.Hosting.OpenAI`, a route-owning server that **exposes an
`AIAgent` (or workflow) as the OpenAI Responses API** (`MapOpenAIResponses` + `IResponsesService`). It
owns the routes, an in-memory response/conversation store, streaming, and lifecycle. The question is
what, if anything, .NET must add to satisfy the ADR-0027 boundary.

## Decision Drivers

- Do not reinvent conversion logic that already exists and is battle-tested in `Hosting.OpenAI`.
- Give applications a way to own their own route/auth/middleware/storage while reusing Agent Framework
  conversion (the ADR-0027 boundary).
- Keep the released public surface small.
- Stay consistent with the existing .NET hosting stack, which deliberately does **not** use the OpenAI
  SDK Responses types server-side (it hand-rolled its own wire model).

## Considered Options

1. Self-contained new package that reimplements conversion using the OpenAI SDK Responses types
   (mirrors the Python `agent-framework-hosting-responses` lineage).
2. New package that reuses `Hosting.OpenAI`'s internal converters (via `InternalsVisibleTo` or by
   moving the conversion core out).
3. Thin public helper facade **inside** `Hosting.OpenAI` over the existing internal converters, plus
   protocol-neutral execution-state holders in `Microsoft.Agents.AI.Hosting`.

### First-principles gap analysis

A capability comparison of the ADR-0027 / PR #6891 helper surface against the existing .NET stack:

| Python helper capability | .NET today | Status |
| --- | --- | --- |
| `responses_to_run` | `ResponseInput.GetInputMessages` + `InputMessage.ToChatMessage` + `OpenAIResponsesMapOptions.RunOptionsFactory` | exists, internal |
| `responses_from_run` | `AgentResponseExtensions.ToResponse` | exists, internal |
| `responses_from_streaming_run` | `AgentResponseUpdateExtensions.ToStreamingResponseAsync` + `SseJsonResult` (also renders workflow events) | exists, internal, richer |
| `responses_session_id` | continuity resolved inside `InMemoryResponsesService` | exists, internal, not standalone |
| `create_response_id` | `IdGenerator` | exists, internal |
| `AgentState` (target + store, get-or-create) | `AgentSessionStore` (get-or-create + save + serialize + isolation) | richer; missing `Delete` + per-session lock |
| `SessionStore` (get/set/delete) | `AgentSessionStore` + `InMemoryAgentSessionStore` | richer; no `Delete` |
| `WorkflowState` + checkpoint resume | `WorkflowCatalog`/`HostedWorkflowBuilder`; workflow events already render over Responses; `CheckpointManager` is session-keyed | partial; no per-session checkpoint cursor |
| App owns routing/auth/middleware/storage | `MapOpenAIResponses`/`IResponsesService` own routing + storage | **the one real gap** |

.NET already covers ~90% of the capability, and more richly (its streaming renderer even emits workflow
events; its session store serializes and supports per-principal isolation, neither of which Python's
in-memory `SessionStore` does). The single genuine gap is the **ownership model**: every conversion
primitive is bundled behind the route-owning server, so an application cannot own its own route and
call just the conversion.

Note on lineage: Python's Responses offering was introduced *as a channel* (PR #6580) and always used
the `openai` SDK Responses types. .NET's `Hosting.OpenAI` predates and is independent of channels and
hand-rolled its own server-side wire DTOs (the SDK's Responses types are client-shaped and awkward
server-side). So Option 1 would both reinvent a working asset and contradict the .NET codebase's own
precedent.

## Decision Outcome

Chosen option: **3. Thin public helper facade inside `Hosting.OpenAI` plus neutral state holders**,
because the only real gap is the ownership model, so the work is to *un-bundle* the existing
converters, not to rebuild them or add a package.

### Public surface

`Microsoft.Agents.AI.Hosting.OpenAI` gains a single public static facade, `OpenAIResponses`, whose
boundary is `System.Text.Json` (`JsonElement`/streamed events), matching Python's dict boundary and
keeping the hand-rolled wire DTOs internal:

- `OpenAIResponses.ToAgentRunRequest(JsonElement body)` -> messages + `AgentRunOptions?`.
- `OpenAIResponses.WriteResponse(AgentRunResponse response, string responseId, string? sessionId = null)`
  -> a Responses-shaped `JsonElement`.
- `OpenAIResponses.WriteResponseStreamAsync(IAsyncEnumerable<AgentRunResponseUpdate> updates, string responseId, ...)`
  -> Responses SSE `data:` frames.
- `OpenAIResponses.GetSessionId(JsonElement body)` -> `previous_response_id` or `conversation` id, or
  `null`. Kept **separate** from `ToAgentRunRequest` so the trust boundary is visible: choosing to use
  a request-derived key is an explicit application decision.
- `OpenAIResponses.CreateResponseId()` -> a `resp_*` id.

All helpers are side-effect-free and delegate to the existing internal converters. `MapOpenAIResponses`
public behavior is unchanged; it and the facade share one internal conversion core (an internal
`ToResponse` overload with an optional originating request is added so the facade can render without a
request object).

### Optional execution state (neutral package)

`Microsoft.Agents.AI.Hosting` gains:

- `AgentSessionStore.DeleteSessionAsync(...)` (+ `InMemoryAgentSessionStore` implementation and
  isolation-decorator passthrough): the one missing store operation.
- `HostedAgentState`: a thin holder bundling an `AIAgent` with an `AgentSessionStore`, exposing
  `GetOrCreateSessionAsync`, `SaveSessionAsync` (including under a newly minted id), and
  `DeleteSessionAsync`, with optional per-session locking. It exists because only the holder has both
  the target and the store; it does not replace `AgentSessionStore`, which already provides
  serialization and isolation that Python's `AgentState`/`SessionStore` lack.
- `HostedWorkflowState`: a thin holder bundling a workflow target with a `CheckpointManager` and an
  internal `sessionId -> CheckpointInfo` head cursor, exposing `RunOrResumeAsync`. .NET's checkpoint
  store is already `sessionId`-keyed (unlike Python's workflow-name keying), but `CheckpointInfo` has
  no ordering, so the holder remembers the head checkpoint per session to resume.

### Scope

Responses only for v1; the facade is named so a parallel `OpenAIChatCompletions` facade can follow.
No new package, no OpenAI-SDK-typed reimplementation, no change to `MapOpenAIResponses` public
behavior.

### Security responsibilities

Consistent with ADR-0027, the application owns the trust boundary. `GetSessionId(...)` returns an
untrusted candidate key; the application must authenticate the caller and authorize/bind the id before
using it as an `AgentSessionStore` key or workflow checkpoint session id. Multi-user hosts must scope
the session store per principal (`IsolationKeyScopedAgentSessionStore`). Helpers stay side-effect-free;
persistence happens only after the run completes.

## Consequences

Positive:

- Smallest possible surface: the released addition is one facade type plus two thin state holders and
  one new store method.
- No duplicated conversion; the app-owned-routing path and the route-owning server share one core.
- `MapOpenAIResponses` users are unaffected.

Negative:

- The facade's `JsonElement` boundary is less strongly typed than the internal DTOs (accepted to keep
  the wire model internal and mirror Python's dict boundary).
- Workflow resume relies on an in-memory head cursor by default; durable multi-replica hosts must
  supply their own cursor persistence.

## More Information

- Parent ADR: [ADR-0027](0027-hosting-channels.md).
- Spec: `docs/specs/003-dotnet-hosting-protocol-helpers.md`.
