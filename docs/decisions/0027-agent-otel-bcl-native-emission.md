---
status: proposed
contact: rogerbarreto
date: 2026-05-26
deciders: sergeymenshykh, rogerbarreto, westey-m, chetantoshniwal
consulted:
informed:
---

# Agent Framework BCL Native OpenTelemetry Emission

## Context and Problem Statement

Agent Framework today emits agent-level OpenTelemetry only when the developer explicitly wraps an agent with `OpenTelemetryAgent` (or `AIAgentBuilder.UseOpenTelemetry()`). Without that wrapping, a bare `ChatClientAgent` is silent even when an OpenTelemetry tracer provider has subscribed to the Agent Framework source. This diverges from common .NET BCL instrumentation shape (e.g. `HttpClient` / `System.Net.Http`), where subscribing to the source via `AddSource(...)` is enough — the library emits natively.

We want telemetry to flow for bare `ChatClientAgent` as soon as a customer subscribes to the existing source `Experimental.Microsoft.Agents.AI`, with no other configuration change. The emitted shape must exactly match what `new OpenTelemetryAgent(agent)` (the current default decorator wrapping) produces today.

## Decision Drivers

- **No customer configuration change.** Existing `AddSource("Experimental.Microsoft.Agents.AI")` must remain the only required step.
- **No regression for explicit `OpenTelemetryAgent` users.** The decorator path must produce exactly the same spans it does today (no duplicates, no shape change). Verified by existing tests `AutoWireChatClient_DefaultsToEnabled_EmitsChatSpan_Async` expecting 2 activities (invoke_agent + chat).
- **Same span shape for bare and decorated paths.** Bare `ChatClientAgent` with `AddSource` must emit the same 2-span (`invoke_agent` + `chat`) shape as today's default decorator.
- **Reuse mature code.** `OpenTelemetryChatClient` already implements the full GenAI semantic conventions. Re-implementing them in agent code would fork the convention and double maintenance.
- **Preserve advanced opt-in behavior.** `OpenTelemetryAgent.EnableSensitiveData` and similar per-instance knobs must remain available.
- **No collision with external libraries** that also use the `gen_ai.operation.name=invoke_agent` semantic convention tag.
- **No suppression of nested sub-agents.** When a `ChatClientAgent` A's tool invokes sub-agent B, B must emit its own `invoke_agent` span (not be suppressed by A's marker).
- **Support metrics-only subscribers.** A user who subscribes only to the Agent Framework meter (no tracer) must still get GenAI metrics.

## Considered Options

- **Option A**: Base `AIAgent` native emission via direct `ActivitySource.StartActivity` in `AIAgent.RunAsync`.
- **Option B**: `ChatClientAgent` self-wrap via `OpenTelemetryAgent(SelfForwardingAgent(this))` at agent level.
- **Option G**: `ChatClientAgent` self-wrap by lazily holding a `new OpenTelemetryAgent(this, defaultSource)` and delegating its `RunCoreAsync` through it (re-entering once with a per-instance marker that prevents recursion).
- **Option C**: `ChatClientAgent : OpenTelemetryAgent` (inheritance).
- **Option D**: Re-implement OTel logic into `ChatClientAgent` from scratch.
- **Option E**: Source-generator hook that wraps every `AIAgent` subclass at compile time.

## Decision Outcome

**Chosen option: Option G (`ChatClientAgent` self-wrap), reusing `OpenTelemetryAgent` directly.**

`ChatClientAgent` lazily caches a `_selfTelemetryWrap` field built as:

```csharp
new OpenTelemetryAgent(this, sourceName: OpenTelemetryConsts.DefaultSourceName)
```

When the agent's `RunCoreAsync` (or `RunCoreStreamingAsync`) is invoked, it checks `SuppressSelfTelemetryWrap()`:

- if the per-agent opt-out `ChatClientAgentOptions.UseProvidedChatClientAsIs` is `true`, OR
- if the default `ActivitySource` has no listeners (`OpenTelemetryConsts.AgentActivitySource.HasListeners() == false`), OR
- if an outer pipeline already owns the `invoke_agent` scope for this specific agent instance (per-instance marker on the parent chain),

then the call delegates straight to `RunChatClientCoreAsync` (the extracted core that performs the actual chat work). Otherwise the call delegates to `_selfTelemetryWrap.RunAsync(...)`, which routes back through `ChatClientAgent.RunCoreAsync` exactly once. On that re-entry the per-instance marker (now set on the outer `invoke_agent` activity by `OpenTelemetryAgent.UpdateCurrentActivity`) is found and the chat work executes.

This produces **a symmetric 2-span shape** (`invoke_agent` + `chat`) for both the bare path and the explicit `new OpenTelemetryAgent(agent)` decorator path, because both paths go through the same `OpenTelemetryAgent` machinery. The auto-wired `OpenTelemetryChatClient` injected by `OpenTelemetryAgent.GetRunOptionsWithChatClientWiring` produces the nested `chat` span on both paths.

Suppression uses a **per-instance** custom property marker on `Activity.Current` and walks the **Activity parent chain** to find it. The marker value is the specific `ChatClientAgent` instance that the outer pipeline covers. The walk handles intermediate Activities (e.g. tool execution spans created by `FunctionInvokingChatClient`) that would otherwise hide the marker.

The marker is set in one place:

- `OpenTelemetryAgent.UpdateCurrentActivity` resolves `this.InnerAgent.GetService<ChatClientAgent>()` and stores that reference as a custom property on the current `invoke_agent` activity.

Both the bare path (where the inner agent is `this` ChatClientAgent that owns the `_selfTelemetryWrap`) and the explicit decorator path (where the user passes their own `OpenTelemetryAgent` over a `ChatClientAgent`) share this single mechanism.

The `HasListeners` fast path is **required**, not optional. Without it, when no tracer subscribes to the default source, `OpenTelemetryChatClient` (inside the auto-wired layer) creates no `Activity`, so `UpdateCurrentActivity` has no activity on which to stamp the marker. The re-entrant call would then find no marker, re-enter the self-wrap, and recurse indefinitely. The fast path means that pure metrics-only subscribers (a meter subscriber without a tracer) do not trigger the self-wrap. This is an accepted trade-off; documented in code comments.

Per-instance scoping correctly distinguishes between:

- **Recursive calls into the same agent** (suppress — outer pipeline already owns the span)
- **Nested sub-agent calls via tools** (do not suppress — different instance, sub-agent emits its own span)

Provider agents that wrap `ChatClientAgent` internally (`FoundryAgent`, `CopilotStudioAgent`, `GitHubCopilotAgent`, `PurviewAgent`, `HarnessAgent`) inherit telemetry transitively. They construct their inner `ChatClientAgent` with the user-facing Id and Name, so the emitted `invoke_agent` span carries the correct identity without any code change in those packages.

Agents without an inner `ChatClientAgent` (`A2AAgent`, `DurableAIAgent`, `WorkflowHostAgent`) are **out of scope** for this ADR. A future ADR may add similar patterns.

### Consequences

- **Good**, because customers keep the same `AddSource("Experimental.Microsoft.Agents.AI")` configuration.
- **Good**, because bare `ChatClientAgent` runs now emit the same 2-span (`invoke_agent` + `chat`) shape as today's `new OpenTelemetryAgent(agent)` default — existing OTel dashboards continue to work.
- **Good**, because the bare path and the explicit decorator path produce identical telemetry — symmetric behavior with no path-specific surprises.
- **Good**, because explicit `OpenTelemetryAgent(ChatClientAgent)` continues to produce exactly the same spans as today (per-instance marker correctly suppresses the inner self-wrap).
- **Good**, because no custom `IChatClient` decorator is added to the codebase — the implementation reuses the existing `OpenTelemetryAgent` and its existing auto-wire factory.
- **Good**, because `OpenTelemetryAgent` decorator remains available for explicit `EnableSensitiveData`, custom source names, and provider-specific enrichment.
- **Good**, because no changes are required to `AIAgent` base, `DelegatingAIAgent`, or any provider package.
- **Good**, because no nested `AIAgent.RunAsync` chains beyond the one re-entry that the existing decorator path already performs — `CurrentRunContext` behavior matches today's decorator path exactly.
- **Good**, because per-instance marker is immune to external library collisions (external code cannot construct a meaningful `ChatClientAgent` reference matching `this`).
- **Good**, because nested sub-agent calls via tools emit their own `invoke_agent` spans (per-instance scoping does not over-suppress).
- **Bad**, because each `ChatClientAgent` instance lazily allocates one `OpenTelemetryAgent` (held until the agent is collected). Per-instance, not per-call.
- **Bad**, because pure metrics-only subscribers (no tracer) do not trigger the self-wrap; users in that mode must explicitly wrap with `OpenTelemetryAgent`.
- **Bad**, because the custom-source bypass edge case (custom source with no listeners + default source with listeners) is documented as known behavior, not fixed in this ADR.
- **Bad**, because per-class opt-in: future non-chat agents won't emit unless they implement their own pattern.

## Validation

- Unit test: bare `ChatClientAgent` with only `AddSource("Experimental.Microsoft.Agents.AI")` emits 2 activities (`invoke_agent` + `chat`) — matching today's `new OpenTelemetryAgent(agent)` default.
- Unit test: `OpenTelemetryAgent → ChatClientAgent` (existing decorated path) emits exactly the same 2 activities as today (no duplicates, no triple emission).
- Unit test: nested sub-agent invocation — agent A's tool calls agent B; both emit their own `invoke_agent` spans (per-instance scoping verified across parent-chain walk).
- Unit test: two sibling `ChatClientAgent` instances each emit their own 2 spans without cross-contamination.
- Unit test: `FoundryAgent`-style provider wrapper (passthrough `DelegatingAIAgent` over inner `ChatClientAgent`) emits 2 activities with the inner agent's user-facing identity.
- Unit test: passthrough `DelegatingAIAgent` does NOT suppress emission (the inner `ChatClientAgent` self-wraps as usual).
- Unit test: `UseProvidedChatClientAsIs = true` suppresses self-wrap; `ChatClientFactory` user hook is still invoked.
- Unit test: streaming path emits 2 activities per `RunStreamingAsync` call.
- Unit test: bare path defaults to NO message content capture (matches `OpenTelemetryChatClient.EnableSensitiveData = false` default).
- Unit test: explicit `OpenTelemetryAgent.EnableSensitiveData = true` continues to capture message content (existing knob unchanged).
- Unit test: lazy init under concurrent first calls produces a single cached `_selfTelemetryWrap` (Interlocked.CompareExchange race verified).
- Unit test: no listeners on default source — zero spans emitted; self-wrap is not allocated.

### Test isolation pattern

Tests in this ADR use a small `OwnerScopedActivityCapture` helper (raw `ActivityListener` + parent-chain marker filter) rather than a global `TracerProvider` + `InMemoryExporter`. This is necessary because OpenTelemetry .NET's listener registry is process-global per source name: two parallel tests both subscribing to `Experimental.Microsoft.Agents.AI` via separate `TracerProvider` instances see each other's activities and corrupt their exporters. The helper filters captured activities by walking each stopped activity's parent chain and only retains those whose chain contains the per-instance marker pointing at the test's owner `ChatClientAgent`. This makes tests fully parallel-safe without serialization attributes and without changing production behavior. The helper lives in the test project only; production users keep using `TracerProvider.AddSource(...)` exactly as today.

## Pros and Cons of the Options

### Option A: Base `AIAgent` native emission

- **Good**, because universal coverage across every `AIAgent` subclass without per-class opt-in.
- **Good**, because matches `HttpClient`'s BCL pattern most literally.
- **Bad**, because re-implements GenAI semantic conventions in `AIAgent` base, forking maintenance from `OpenTelemetryChatClient`.
- **Bad**, because produces a thinner `invoke_agent` span (only basic agent.* tags) — different shape from today's default.
- **Bad**, because requires a service-key suppression contract across the `Microsoft.Agents.AI.Abstractions` → `Microsoft.Agents.AI` assembly boundary.

### Option B: `ChatClientAgent` self-wrap at agent level via SelfForwardingAgent

- **Good**, because reuses mature `OpenTelemetryAgent` plumbing as-is.
- **Bad**, because creates nested `AIAgent.RunAsync` chains — `CurrentRunContext` oscillates between three agent identities under streaming (each yield resets context at every nesting level).
- **Bad**, because `OpenTelemetryAgent` is `IDisposable` and the hidden instance per `ChatClientAgent` adds disposal lifetime concerns.
- **Bad**, because the ambient boolean marker on `Activity.Current` over-suppresses nested sub-agent calls.

### Option G: `ChatClientAgent` self-wrap reusing `OpenTelemetryAgent` (recommended)

- **Good**, because reuses `OpenTelemetryAgent` end-to-end — no new instrumentation code in `ChatClientAgent`. The existing auto-wired `OpenTelemetryChatClient` handles all GenAI semconv tracking (token usage, response metadata, model, finish reasons, provider name).
- **Good**, because the bare path and the explicit decorator path go through the exact same code, producing identical telemetry shape (no path-specific surprises).
- **Good**, because per-instance marker (`ChatClientAgent` reference + `ReferenceEquals`) avoids external library collisions and avoids over-suppressing nested sub-agents.
- **Good**, because two-span shape matches today's `new OpenTelemetryAgent(agent)` default exactly.
- **Good**, because the only behavioral change at the call boundary is `ChatClientAgent` delegating its run to a cached `OpenTelemetryAgent` instance, which then re-enters `ChatClientAgent` exactly once with the marker in place.
- **Bad**, because per-class opt-in: future non-chat agents won't emit unless they implement their own pattern.
- **Bad**, because each `ChatClientAgent` lazily allocates one `OpenTelemetryAgent` (per-instance, not per-call).
- **Bad**, because pure metrics-only subscribers (no tracer) bypass the self-wrap — the `HasListeners` fast path is required to prevent infinite recursion in that scenario.
- **Bad**, because narrow custom-source edge case (custom source no listeners + default source with listeners → self-wrap emits on default).

### Option C: `ChatClientAgent : OpenTelemetryAgent` (inheritance)

- **Bad**, because inverts the wrapper/wrappee relationship.
- **Bad**, because `OpenTelemetryAgent` is `sealed` today.
- **Bad**, because doesn't cover `FoundryAgent`, `A2AAgent`, or any other concrete agent.

### Option D: Re-implement OTel logic into `ChatClientAgent` from scratch

- **Bad**, because duplicates `OpenTelemetryChatClient`'s semconv implementation.
- **Bad**, because doubles maintenance.

### Option E: Source-generator hook

- **Bad**, because the repository has no existing source-generator infrastructure.
- **Bad**, because source generators have IDE tooling and trimming complications.

## More Information

This ADR is additive to ADR 0026. ADR 0026 describes activation extensions and DI auto-wrap. ADR 0027 defines the baseline native emission behavior so that the source subscription alone is sufficient.

Implementation details (file-by-file change shape, shared-source-file wiring, prototype walkthrough) live in the session plan document.

### Per-instance marker mechanism

The custom property key is `internal const string OpenTelemetryConsts.OwnedInvokeAgentScopeMarker = "Microsoft.Agents.AI.OpenTelemetry.OwnedInvokeAgentScope"`. The value stored is a `ChatClientAgent` reference.

Suppression check (inside `ChatClientAgent.SuppressSelfTelemetryWrap`):
```csharp
for (var act = Activity.Current; act is not null; act = act.Parent)
{
    if (ReferenceEquals(act.GetCustomProperty(OpenTelemetryConsts.OwnedInvokeAgentScopeMarker), this))
    {
        return true;  // an outer pipeline (this agent's own self-wrap, or a user's OpenTelemetryAgent decorator) already owns invoke_agent for this specific agent
    }
}
return false;
```

The walk handles intermediate Activities (e.g. `FunctionInvokingChatClient` tool execution spans) that would otherwise hide the marker. Cost is O(depth) — typically 3-5 levels, each a dictionary lookup with no allocations.

The marker is set in exactly one place, on the outer `invoke_agent` activity, by `OpenTelemetryAgent.UpdateCurrentActivity`:

```csharp
var inner = this.InnerAgent.GetService<ChatClientAgent>();
if (inner is not null)
{
    activity.SetCustomProperty(OpenTelemetryConsts.OwnedInvokeAgentScopeMarker, inner);
}
```

This unifies both the bare path (the `OpenTelemetryAgent` was created by `ChatClientAgent` itself, wrapping `this`) and the explicit decorator path (the user constructed `new OpenTelemetryAgent(myChatClientAgent)`). The `ReferenceEquals` check ensures nested sub-agent calls (different instances) are not suppressed.

Custom properties are process-local state on `Activity` instances. They are NOT exported as OTLP span attributes — verified against OpenTelemetry .NET `ProtobufOtlpTraceSerializer.WriteActivityTags` which only serializes tags from `activity.EnumerateTagObjects()`. The marker has no impact on telemetry payload.

### Why the `HasListeners` fast path is mandatory

If `OpenTelemetryConsts.AgentActivitySource.HasListeners()` returns `false`, `ChatClientAgent.SuppressSelfTelemetryWrap` returns `true` immediately and the self-wrap is skipped. This is not just an optimization — it is required for correctness.

When no tracer subscribes to the default source, the auto-wired `OpenTelemetryChatClient` inside `OpenTelemetryAgent.GetRunOptionsWithChatClientWiring` produces no `Activity` (its own `HasListeners` check short-circuits). With no activity, `OpenTelemetryAgent.UpdateCurrentActivity` has nothing to stamp the marker on. The re-entrant call to `ChatClientAgent.RunCoreAsync` from inside the self-wrap would then find no marker, re-enter the self-wrap, and recurse indefinitely — verified by stack overflow during initial implementation.

The trade-off: pure metrics-only subscribers (a meter subscriber without a tracer) do not trigger the self-wrap. Such users must explicitly wrap with `new OpenTelemetryAgent(agent)` to get metric emission. This is acceptable because metrics-only configurations are rare and the explicit wrap remains available.

### Known edge case: custom source with no listeners

If a user wraps with `new OpenTelemetryAgent(agent, "MyCustomSource")` AND `MyCustomSource` has no listeners AND the default `Experimental.Microsoft.Agents.AI` source has listeners:

1. `OpenTelemetryAgent._otelClient` (on `MyCustomSource`) creates no Activity (no listeners).
2. `UpdateCurrentActivity` bails because there is no Activity to attach the marker to.
3. Inner `ChatClientAgent.ResolveEffectiveChatClient` sees no marker → self-wraps on default source.
4. User unexpectedly sees telemetry on the default source.

**Mitigation**: customers using custom source names should subscribe only to their custom source. A future ADR may add an opt-out API on `ChatClientAgentOptions` (e.g. `SuppressAutoTelemetry`) for users who need explicit control.

### Cross-language comparison

Python today applies telemetry via mixin layers on individual agent classes:

| Python class | AgentTelemetryLayer | Chat-client based |
| --- | --- | --- |
| `Agent` | yes | yes (extends `RawAgent`) |
| `A2AAgent` | yes | NO (extends `BaseAgent`) |
| `ClaudeAgent` | yes | yes (extends `RawClaudeAgent`) |
| `FoundryAgent` | yes (also `ChatTelemetryLayer`) | yes |
| `GitHubCopilotAgent` | yes | yes |
| `RawAgent` | NO | yes (opt-out path) |
| `WorkflowAgent` | NO | no (silent failure mode) |

Python's `Agent` is not the direct equivalent of .NET's `ChatClientAgent`. The closer mapping is:

| Concept | Python | .NET today | .NET after ADR 0027 |
| --- | --- | --- | --- |
| Chat-client agent without telemetry | `RawAgent` | `ChatClientAgent` | `ChatClientAgent` with no `AddSource(...)` |
| Chat-client agent with telemetry | `Agent` (RawAgent + mixin) | `new OpenTelemetryAgent(new ChatClientAgent(...))` | `ChatClientAgent` with `AddSource(...)` |
| Provider agent with telemetry | e.g. `FoundryAgent` (mixin) | `OpenTelemetryAgent(FoundryAgent)` decorator | `FoundryAgent` with `AddSource(...)` (transitively via inner `ChatClientAgent`) |
| Sensitive data toggle | global `OBSERVABILITY_SETTINGS.enable_sensitive_data` via `ENABLE_SENSITIVE_DATA` env var | per-decorator `OpenTelemetryAgent.EnableSensitiveData` | unchanged: stays on advanced `OpenTelemetryAgent` path |

Both Python and ADR 0027 (.NET) share the same characteristic that non-chat-client agents must explicitly opt in to telemetry. Python does this via the `AgentTelemetryLayer` mixin; .NET will do this by adding a self-wrap pattern in each agent class that needs it (only `ChatClientAgent` in this ADR).

### Known telemetry richness gap (future ADR)

Because Option G reuses `OpenTelemetryAgent` directly, the bare path emits exactly the same shape as today's `new OpenTelemetryAgent(agent)` default: an outer `invoke_agent` span and a nested `chat` span produced by the auto-wired `OpenTelemetryChatClient`. There is no richness delta between the bare path and the decorator path after this ADR.

A small gap remains versus Python, which captures a few additional things on the `invoke_agent` span that .NET still does not on either path:

| Span attribute | Python invoke_agent | .NET invoke_agent (after ADR 0027) |
| --- | --- | --- |
| Aggregated token usage across multiple round-trips in function-calling loops | yes | partial (whatever `OpenTelemetryChatClient` captures per request) |
| Input messages (when sensitive enabled) | yes | only on chat span |
| Output messages (when sensitive enabled) | yes | only on chat span |
| System instructions | yes | NO |
| Conversation/thread id | yes | NO |
| All chat options (model, temperature, etc.) | yes | only on chat span |
| Operation duration histogram metric | yes | NO |

A future ADR (placeholder `0028-agent-otel-invoke-span-enrichment`) will propose closing this remaining gap by enriching the `invoke_agent` span produced by `OpenTelemetryAgent` itself, which will benefit both paths uniformly. This ADR scope is intentionally narrow: produce native emission with no regression and no customer configuration change, matching today's `new OpenTelemetryAgent(agent)` default exactly.
