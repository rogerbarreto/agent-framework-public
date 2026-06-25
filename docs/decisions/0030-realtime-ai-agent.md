---
status: proposed
contact: rogerbarreto
date: 2026-06-24
deciders: rogerbarreto
consulted:
informed:
---

# Realtime Agent over Microsoft.Extensions.AI `IRealtimeClient`

## Context and Problem Statement

Microsoft.Extensions.AI (MEAI) ships an experimental realtime abstraction
(`IRealtimeClient` / `IRealtimeClientSession`, marked `[Experimental(AIRealTime)]`) that models a
full-duplex, bidirectional voice and audio session: the client streams audio up while the server
streams audio, transcription, function calls, and lifecycle events down at the same time, with
server-side voice activity detection (VAD) and barge-in. Today only `Microsoft.Extensions.AI.OpenAI`
ships a released adapter; Google Gemini Live, Vertex AI, and AWS Bedrock Nova Sonic adapters exist
upstream but are unreleased, and OpenAI-wire-compatible providers (Azure VoiceLive, xAI) reuse the
OpenAI realtime wire format.

Agent Framework (MAF) has no realtime agent today. We want a `RealtimeAgent` that sits on top of
`IRealtimeClient` the same way `ChatClientAgent` sits on `IChatClient`, so that realtime voice
becomes a first-class agent capability that still composes with the rest of the framework
(middleware, multi-agent orchestration, handoffs, telemetry).

The core architectural question: should realtime require a **new agent base abstraction** because its
duplex lifecycle differs from the request and response model of `AIAgent`, or can we **reuse
`AIAgent`** and add realtime-specific APIs on top?

## Decision Drivers

- **Composition**: realtime agents should compose with existing `AIAgent` infrastructure
  (`DelegatingAIAgent`, `OpenTelemetryAgent`, orchestration, handoffs) which all expect `AIAgent`.
- **Consistency with `ChatClientAgent`**: follow the established convention of specializing the
  session and options while keeping base response types, with the provider construct reachable via
  `GetService`.
- **Duplex fidelity**: the full-duplex experience (continuous audio, barge-in, server-initiated
  events) must be expressible, not flattened away.
- **No leaking of provider types**: agent-level APIs should expose agent-level abstractions, not raw
  MEAI types, the same way `AgentResponse` wraps `ChatResponse`.
- **Full transparency, nothing swallowed**: every signal that flows from the MEAI abstraction must be
  representable at the agent level, with a break-glass path to the raw MEAI and provider SDK objects.
- **Low duplication**: reuse the MEAI realtime middleware pipeline (`RealtimeClientBuilder` with
  function invocation, OpenTelemetry, logging) rather than rebuilding transport or the tool loop.

## Considered Options

- **Option 1: New dedicated realtime base abstraction** (`RealtimeAgent` not deriving from
  `AIAgent`), modeling the duplex lifecycle natively.
- **Option 2 (preferred): Reuse `AIAgent` with a `RealtimeAgent` that adds realtime-native APIs.**
  The base `AIAgent` surface provides a half-duplex experience; an additional agent-level conversation
  abstraction exposes the full-duplex surface.

## Decision Outcome

Chosen option: **Option 2, reuse `AIAgent`**, because a half-duplex experience can be represented
inside a full-duplex environment, so the simple agent experience comes for free on the base surface
and the full-duplex surface is added on top without forking the agent model or losing composition.

`RealtimeAgent : AIAgent` wraps an `IRealtimeClient` and exposes two surfaces.

### Half-duplex surface (base `AIAgent`)

`RunAsync` and `RunStreamingAsync` give a request and response experience:

- Open a fresh ephemeral `IRealtimeClientSession` per run (decision Q2 / 2a). Server-side continuity
  across runs is achieved by replaying the `AgentSession` history into the new socket, the same way
  chat agents rehydrate. A persistent warm connection is deferred (see core-team sub-options).
- Accept both audio and text input (decision Q2 / audio input A): text in a `ChatMessage` becomes an
  input-text conversation item, and audio as `DataContent` becomes an input audio buffer append plus
  commit, followed by a response request.
- Drain the server message stream until `ResponseDone`, projecting to base `AgentResponse` and
  `AgentResponseUpdate` (audio as `DataContent`, transcription as text, raw realtime message in
  `RawRepresentation`). One agent run maps to one realtime turn.

### Full-duplex surface (agent-level conversation)

A new agent-level abstraction `AgentConversation` represents a live duplex exchange, mirroring how
`AgentResponse` wraps `ChatResponse` (decision Q1 / 1b and Q6.2 / 6.2a):

- `AgentConversation` is a single concrete, reusable agent-level type implementing `IAsyncDisposable`.
  It is **not** an `AgentSession` (that type is serialization-oriented and not disposable; a live
  socket is neither serializable nor free to hold).
- It exposes agent-level operations only: `SendAsync(ChatMessage)`, `SendAudioAsync(DataContent)`,
  `InterruptAsync()`, and a single output stream of `AgentResponseUpdate` (decision Q3 / 3b). The
  single-reader constraint of `IRealtimeClientSession.GetStreamingResponseAsync` is honored by the
  conversation owning the one read loop.
- No raw MEAI types appear on agent-level APIs. The underlying `IRealtimeClientSession` and provider
  SDK object remain reachable through `RawRepresentation` and `GetService` (break-glass).
- `RealtimeAgent.StartConversationAsync(RealtimeAgentRunOptions?)` returns the `AgentConversation`.
- There is no `RealtimeAgentConversation` subclass, mirroring the absence of a `ChatClientAgentResponse`.

### Full signal representation, nothing swallowed (decision Q4)

Every `RealtimeServerMessage` is represented as an `AgentResponseUpdate`. Content-bearing events map
to typed `AIContent` (audio to `DataContent`, transcription to `TextContent`, tool call to
`FunctionCallContent`). Pure signals with no content payload (VAD speech-started and speech-stopped,
barge-in, response-created and response-done, input-transcription, error) are surfaced as a typed
realtime event content carried in `AgentResponseUpdate.Contents`. No event is dropped. The
`RawRepresentation` chain stays transparent: `AgentResponseUpdate` to `RealtimeServerMessage` to the
provider SDK object, matching the existing Agent to MEAI to SDK path.

### Specialization (matches MAF convention)

- Specialize the **session**: `RealtimeAgentSession : AgentSession` holds serializable realtime state
  (for example conversation id and resumption token) used to rehydrate ephemeral half-duplex runs.
- Specialize the **options**: `RealtimeAgentRunOptions : AgentRunOptions` maps to
  `RealtimeSessionOptions` (voice, VAD, audio formats, tools).
- Keep base `AgentResponse` and `AgentResponseUpdate` enriched through `RawRepresentation`. No new
  response subclass.
- Reuse the MEAI middleware pipeline via `RealtimeClientBuilder` (function invocation, OpenTelemetry,
  logging).
- Expose the underlying construct two ways, as `ChatClientAgent` does for `IChatClient`: a public
  `RealtimeClient` property and `GetService(typeof(IRealtimeClient))`.

### Provider scope (decision Q5)

OpenAI is the primary reference adapter (released `Microsoft.Extensions.AI.OpenAI`). Google Gemini
Live is the protocol-different portability proof, validating that the abstraction is not OpenAI-shaped.
The Gemini Live adapter is pre-release upstream, which is acceptable because the `RealtimeAgent`
package itself ships as preview and pre-release with no GA planned yet, consistent with the
`[Experimental]` status of the MEAI realtime abstraction.

### Packaging and naming

- Agent type: `RealtimeAgent` (convention for direct `AIAgent`-derived provider agents, no "AI" infix).
- New agent-level abstraction: `AgentConversation`.
- Package: `Microsoft.Agents.AI.Realtime` for the realtime agent. `AgentConversation`, being a general
  agent-level abstraction, is proposed for `Microsoft.Agents.AI.Abstractions` (see sub-options).

### Consequences

- Good, because realtime agents compose with all existing `AIAgent` infrastructure unchanged.
- Good, because the design mirrors `ChatClientAgent` and `AgentResponse`, so the mental model and
  extension points are already familiar.
- Good, because no provider types leak into agent-level APIs while break-glass keeps the path
  transparent.
- Good, because no realtime signal is swallowed.
- Good, because transport and the tool loop are reused from MEAI rather than reimplemented.
- Neutral, because `AgentConversation` is a new live, disposable abstraction the framework has not had
  before; its lifetime and concurrency semantics must be defined carefully.
- Bad, because the base half-duplex surface hides a stateful socket and per-run handshake cost, which
  must be documented so callers are not surprised.

## Validation

Validated by code review of this ADR and by unit and integration tests exercising both surfaces: a
half-duplex agent run that completes on `ResponseDone`, and a full-duplex `AgentConversation` that
streams audio in and out with barge-in and surfaces every signal. Portability is validated by running
the same `RealtimeAgent` against OpenAI realtime and Gemini Live.

## Sub-options for core-team agreement

- **Where realtime signal and event content types live**: upstream in MEAI (preferred, so all
  `IRealtimeClient` consumers share them) versus a MAF-side type as a fallback if upstream is slow.
- **Home of `AgentConversation`**: `Microsoft.Agents.AI.Abstractions` as a general agent-level
  abstraction (preferred) versus scoping it to the realtime package initially.
- **Warm half-duplex connection (option 2c)**: keep ephemeral per-run as the v1 default, with an
  opt-in reuse of an already-open `AgentConversation` bound to the `AgentSession` as a documented
  future optimization.
- **Second portability provider**: Gemini Live (chosen) versus AWS Bedrock Nova Sonic, depending on
  upstream adapter readiness.

## Pros and Cons of the Options

### Option 1: New dedicated realtime base abstraction

A `RealtimeAgent` base type that does not derive from `AIAgent`, modeling the duplex lifecycle natively.

- Good, because the API can be duplex-native without any half-duplex projection.
- Good, because the lifecycle of a long-lived session is explicit in the type from the start.
- Neutral, because it could later be bridged to `AIAgent` with an adapter.
- Bad, because it loses direct composition with `DelegatingAIAgent`, `OpenTelemetryAgent`,
  orchestration, and handoffs, all of which expect `AIAgent`.
- Bad, because it forks the agent model into two parallel hierarchies, increasing surface area and
  long-term maintenance cost.
- Bad, because users must learn a second, separate agent concept for voice.

### Option 2 (preferred): Reuse `AIAgent` with realtime-native additions

`RealtimeAgent : AIAgent` with a half-duplex base surface and an additive agent-level
`AgentConversation` for full duplex, as described in the Decision Outcome.

- Good, because it preserves full composition with existing `AIAgent` infrastructure.
- Good, because it is consistent with the `ChatClientAgent` to `IChatClient` and `AgentResponse` to
  `ChatResponse` patterns, including specializing session and options and exposing the provider
  construct via `GetService`.
- Good, because half-duplex is free and full-duplex is one method away.
- Good, because agent-level abstractions hide MEAI types while break-glass keeps full transparency.
- Good, because it reuses the MEAI realtime middleware pipeline.
- Neutral, because `AgentConversation` and `RealtimeAgentSession` are richer than today's
  serialization-oriented sessions.
- Bad, because the base surface conceals a stateful connection and per-run handshake cost, which needs
  clear documentation.

## More Information

- MEAI realtime abstractions: `IRealtimeClient`, `IRealtimeClientSession` (`IAsyncDisposable`,
  `SendAsync`, single-reader `GetStreamingResponseAsync`, `GetService`, `Options`),
  `RealtimeSessionOptions`, `RealtimeSessionKind` (Conversation and Transcription),
  `RealtimeServerMessage` and `RealtimeClientMessage` hierarchies, `RealtimeClientBuilder` with
  `UseFunctionInvocation`, `UseOpenTelemetry`, `UseLogging` (all `[Experimental(AIRealTime)]`).
- Provider landscape at time of writing: released adapter is `Microsoft.Extensions.AI.OpenAI` only;
  Gemini Live, Vertex AI, and Bedrock Nova Sonic adapters exist upstream but are unreleased; Azure
  VoiceLive and xAI reuse the OpenAI realtime wire format.
- Reference for the chosen conventions: `ChatClientAgent`
  (`dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgent.cs`) returns base `AgentResponse` and
  specializes `ChatClientAgentSession` and `ChatClientAgentRunOptions`; `AgentResponse`
  (`dotnet/src/Microsoft.Agents.AI.Abstractions/AgentResponse.cs`) wraps `ChatResponse` via
  `RawRepresentation`.
- This decision can be revisited if the MEAI realtime abstraction stabilizes in a way that changes the
  session lifecycle, or if a duplex-native base proves necessary for advanced scenarios.
