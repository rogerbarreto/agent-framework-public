---
status: proposed
contact: RogerBarreto
date: 2026-05-28
deciders: RogerBarreto
informed: eavanvalkenburg, agent-framework dotnet contributors
---

# .NET hosting core and pluggable channels

> **Posture: translate.** The Python spec [`002-python-hosting-channels.md`](./002-python-hosting-channels.md) is the canonical source of truth for vocabulary, channel taxonomy, identity model, response targets, wire formats, durable-runner posture, and cross-channel continuity semantics. This spec describes how those concepts are translated into idiomatic .NET (`IHostApplicationBuilder` + `IEndpointRouteBuilder` composition on top of ASP.NET Core / Generic Host) and the specific package layout, type names, and lifecycle that the .NET implementation will ship. Where this spec is silent on a behavioral question, the Python spec governs.

## What are the business goals for this feature?

Give .NET app authors one low-level hosting surface that can expose a single **hostable target** — either an `AIAgent` or a `Workflow` (or a Foundry hosted-agent handle via a swappable runner) — on one or more **channels** (Responses API, Invocations API, Telegram, and future Discord / Activity Protocol / etc.) without writing per-protocol routing or server glue, **and** let an end user start a conversation on one channel and seamlessly continue it on another against the same target and the same conversation history.

This consolidates the per-protocol .NET hosting packages that exist today (`Microsoft.Agents.AI.Hosting.OpenAI`, `.Hosting.A2A(.AspNetCore)`, `.Hosting.AGUI.AspNetCore`, `.Hosting.AzureFunctions`, `.Foundry.Hosting`) into a shared composable model where:

- a single `AgentFrameworkHost` owns ASP.NET Core endpoints (or `IHostedService` lifecycle when no HTTP is required) and channels own protocol shape;
- session identity is **channel-neutral** — channel-supplied native ids are mapped to a stable `IsolationKey` so two channels mounted on the same host can resolve to the **same** `AgentSession` for the same end user;
- channel-native identity is **mapped, not assumed** — the host exposes `IIdentityAllowlist` / `IIdentityLinker` seams (channel-native id → isolation key, plus a one-time-code / OAuth / MFA link ceremony) so cross-channel continuity does not depend on namespaces happening to align;
- response delivery is **decoupled from request origin** — every `ChannelRequest` carries a `ResponseTarget` (`Originating` (default), `Active`, a specific channel, all linked channels, or `None`), so long-running runs can return their result on a different channel than the one that started them;
- channels can be assigned different **confidentiality tiers** so two channels on one host can share an agent without sharing a session;
- **multi-user surfaces** (Telegram groups, forum topics; future Teams channels) are first-class — channels separate user identity from conversation locator with safe defaults (`MentionOnly` addressing, per-user-per-conversation session scoping, link ceremonies redirected to DMs).

**Success criteria:**

- A basic multi-channel sample requires only one `builder.AddAgentFrameworkHost(target).AddXxxChannel(...).AddYyyChannel(...)` chain and a single `app.MapAgentFrameworkHost()` call. No hand-written protocol routes, no per-protocol host bootstrap.
- A single `AgentFrameworkHost` configured with `ResponsesChannel` + `TelegramChannel` can be exercised by one end user across both and observe one continuous conversation.
- A user known on one channel can run a host-provided `/link` command on a second channel, complete a one-time-code ceremony, and see subsequent messages on the second channel resolved against the same `AgentSession` as the first.
- A user can submit a long-running run on Telegram with `ResponseTarget = Active`, switch to another channel (Responses, future Activity), and receive the result there as a proactive push — with a poll route as fallback.

## What is the problem being solved?

### How do .NET developers solve this today?

Every protocol surface is its own package with its own `Map*` extension. A developer who wants to expose one agent over both the OpenAI Responses API and a webhook channel has to stand up two hosts and stitch them together by hand:

```csharp
// Today: developer composes per-protocol Map* calls and writes any non-supported transport by hand.
var builder = WebApplication.CreateBuilder(args);
builder.AddAIAgent(sp => new AzureOpenAIChatClient(...).CreateAIAgent(name: "Weather", instructions: "..."));

var app = builder.Build();
app.MapOpenAIResponses("/responses", agentName: "Weather");  // package: Hosting.OpenAI
app.MapA2A("/a2a", agentName: "Weather");                     // package: Hosting.A2A.AspNetCore
app.Run();
```

Adding a Telegram bot, Discord bot, or Teams entry point requires leaving this stack entirely: standing up a separate worker, installing a channel SDK, hand-writing the polling/webhook loop, mapping every native update into an `AIAgent.RunAsync` call, and bolting on commands (`/start`, `/new`, `/cancel`, …) — none of which is reusable across other channels. Identity, session continuity, response targeting, and proactive push do not exist as cross-cutting concerns: each developer reinvents them per integration.

### Why does this problem require a new hosting abstraction?

The gap is between **owning a hostable target** (an `AIAgent` or a `Workflow`) and **operationalizing it on multiple channels**. Agent Framework already provides agents, workflows, sessions, run inputs, response/update streaming, the `AIAgent` execution seam, and the `Workflow` execution seam. What's missing is a generic host that:

1. Owns one ASP.NET Core endpoint surface (or pure-worker `IHostedService` set) and one set of lifecycle hooks.
2. Lets channels contribute routes, commands, and startup/shutdown without protocol leakage into the host.
3. Standardizes how protocol requests become agent invocations (input, options, session, streaming) and how results flow back out — including proactive push for non-`Originating` response targets.
4. Owns the identity stack (resolution, linking, authorization, isolation) once instead of per channel.
5. Owns durable continuation, host state (link grants, active-channel ledger, continuation tokens), and isolation-key context propagation once instead of per channel.

Python has already built this model on `feature/python-hosting`. .NET needs the equivalent so the same agent can be reached over Responses, Invocations, and Telegram simultaneously, resolve to the same session per user, and let third parties ship new channel packages without forking the host.

## Decisions

The full grilling log lives in the session glossary. The bullets below summarize what is locked.

1. **Posture: translate.** Python is canonical. .NET ports the vocabulary, taxonomy, and wire formats faithfully; deviates only where ASP.NET Core mechanics force it.
2. **Composition: builder-centric.** Single happy path: `builder.AddAgentFrameworkHost(target).AddXxxChannel(...)` then `app.MapAgentFrameworkHost()`. No standalone `Map*` extensions in the new packages.
3. **Channel contract: `abstract class Channel` + capability interfaces.** `Channel` is an abstract class with three members (`Name`, `Path`, `Contribute`) so it can grow virtual members non-breakingly. `ChannelContribution` is a record with init-only properties (4 fields: `Routes`, `Commands`, `OnStartup`, `OnShutdown`). Optional cross-cutting capabilities (`IChannelPush`, `IChannelPushCodec`, `IChannelRunHook`, `IChannelResponseHook`, `IChannelStreamTransformHook`, `IConfidentialityTagged`) live as small separate interfaces a channel mixes in.
4. **Channel lifecycle: two-phase split.** `Channel.ConfigureServices(IServiceCollection)` runs at `AddXxxChannel(...)` time (pre-`Build`); `Channel.Contribute(IChannelContext)` runs at `MapAgentFrameworkHost(...)` time (post-`Build`). Matches the long-standing ASP.NET Core `ConfigureServices` + `Configure` split.
5. **Foundry reuse: swappable `IHostedTargetRunner`.** The host registers one runner based on what was passed to `AddAgentFrameworkHost(...)`: `AIAgentRunner` for `AIAgent`, `WorkflowRunner` for `Workflow`, and `Microsoft.Agents.AI.Foundry.Hosting` ships `FoundryHostedAgentRunner` for a remote Foundry hosted-agent handle. Channels never branch on target type.
6. **Identity & authorization: literal port.** Ship our own `IIdentityAllowlist`, `IIdentityLinker`, `AuthorizationContext`, `AllowlistDecision` enum (`Allow` / `Deny` / `Abstain`), and combinators (`AnyOfIdentityAllowlist`) independent of `Microsoft.AspNetCore.Authorization`. Reasons: (a) the Python model has domain shapes (pre/post-link evaluation, abstain tri-state, cross-channel any-of combinator, native-id vs linked-claim) that don't map onto ASP.NET's request-scoped `IAuthorizationHandler` cleanly; (b) non-HTTP channels (Telegram polling, future Discord gateway) have no `HttpContext`; (c) keeps the channel-author packages ASP.NET-free. An optional `AspNetCoreIdentityAllowlistAdapter` shim can be added later for app authors who want to bridge their existing `AuthorizationPolicy` objects.
7. **Host state store: new `IHostStateStore`, separate from `AgentSessionStore`.** Existing `AgentSessionStore` keys per `(AIAgent, conversationId)` — doesn't fit the new state (identity registry, identity-link grants, active-channel ledger, continuation tokens). Ship `InMemoryHostStateStore` and `FileHostStateStore` in v1, configured via `HostStatePathOptions` (optional `Root` shorthand plus optional per-component path overrides — `RunnerPath`, `LinksPath`, `ContinuationsPath`, `LastSeenPath`). Workflow checkpoint storage is **not** an `IHostStateStore` concern (it stays on `WorkflowBuilder.CheckpointStorage` per decision 13). New components add new optional properties non-breakingly. Mirrors Python's `HostStatePaths` TypedDict, which is brand new with this work.
8. **Durable runner: own `IDurableTaskRunner` seam + in-process default + opt-in DTF adapter.** Channels core defines `IDurableTaskRunner` (4 methods: `ScheduleAsync`, `GetAsync`, `CancelAsync`, `ResumeAsync`). `InProcessDurableTaskRunner` ships in core as an `IHostedService` + bounded `Channel<T>` consumer; in-memory unless `HostStatePathOptions.RunnerPath` is set, in which case records persist to disk and replay on `ResumeAsync`. A separate opt-in package `Microsoft.Agents.AI.Hosting.Channels.DurableTask` ships `DurableTaskFrameworkRunner` that wraps the existing `Microsoft.Agents.AI.DurableTask` package for ephemeral runtime modes.
9. **Hosting target: Generic Host + ASP.NET Core.** `AddAgentFrameworkHost(this IHostApplicationBuilder, target)` accepts both `WebApplicationBuilder` (which derives from `IHostApplicationBuilder`) and `HostApplicationBuilder` (pure worker). HTTP routes via `app.MapAgentFrameworkHost(this IEndpointRouteBuilder)`. Non-HTTP channels (Telegram polling, future Discord gateway) auto-start via `IHostedService`. If a registered channel requires `IEndpointRouteBuilder` and the host doesn't have one (pure worker), startup throws a clean error.
10. **Naming: literal port.** Host type = `AgentFrameworkHost`. Builder extensions = `AddAgentFrameworkHost(...)` and `MapAgentFrameworkHost(...)`. Channel-add extensions = `AddResponsesChannel(...)`, `AddInvocationsChannel(...)`, `AddTelegramChannel(...)`. Matches Python class name 1:1; follows the `AddOpenTelemetry` / `AddSignalR` precedent. Always fully qualified (never just `Host`) to avoid colliding with `Microsoft.Extensions.Hosting.Host`.
11. **Packaging: one assembly per channel.** v1 NuGet packages: `Microsoft.Agents.AI.Hosting.Channels` (core), `.Responses`, `.Invocations`, `.Telegram`. `Microsoft.Agents.AI.Foundry.Hosting` gains `FoundryHostedAgentRunner` as an additive type (no break to existing surface). Fast-follow packages: `.Channels.DurableTask`, `.Channels.Discord`, `.Channels.Activity`, `.Channels.EntraId`.
12. **Isolation context propagation: static `IsolationKeys.Current` + DI `IIsolationKeysAccessor`, both backed by `AsyncLocal<IsolationKeys?>`.** Distinct from the app-level isolation key produced by `IIdentityResolver`. `IsolationKeys` carries the Foundry runtime's per-request partition hints lifted off `x-agent-user-isolation-key` / `x-agent-chat-isolation-key` headers by ASP.NET Core middleware the host registers automatically. *Providers* (a future Foundry-partitioned history/state store) read `IsolationKeys.Current` to scope backend calls. Channels themselves are oblivious. Header names are the literal port for wire compat with Python. **v1 ships the plumbing only**; no Foundry-aware provider consumes it yet.
13. **Workflow channel surface: workflow-agnostic channels + one InvocationsChannel convenience.** Channels never branch on whether the target is an `AIAgent` or a `Workflow`. The workflow story is carried by (a) `WorkflowRunner : IHostedTargetRunner`, (b) generic `HostedRunResult<TResult>` with `TResult = WorkflowRunResponse` for workflow targets, (c) free-form `ChannelRequest.Attributes` carrying workflow-specific knobs (reserved keys: `workflow.checkpoint_id`, `workflow.resume_token`), (d) workflow checkpoint storage stays on `WorkflowBuilder` plumbing — `IHostStateStore` does **not** manage workflow checkpoints. One convenience hook ships for v1: `WorkflowInvocationsResponseHook` in `.Hosting.Channels.Invocations` renders `RequestInfoEvent` as a standard envelope `{ "status": "awaiting_input", "request": {...}, "resume_token": "..." }`. App authors handle `RequestInfoEvent` rendering for Telegram / Responses via their own `IChannelResponseHook`.
14. **v1 scope: net-new only; existing extensions untouched.** v1 ships `ResponsesChannel` + `InvocationsChannel` + `TelegramChannel` on the new builder, plus core infrastructure, plus the `FoundryHostedAgentRunner` adapter in `Foundry.Hosting`. **Existing `Hosting.OpenAI` / `Hosting.A2A` / `Hosting.AGUI.AspNetCore` / `Hosting.AzureFunctions` / `Foundry.Hosting` `Map*` extensions stay completely untouched — no `[Obsolete]`, no rewrite, no shim.** Mirrors Python's v1 stance (A2A / AGUI / DevUI are explicitly out of scope for first implementation). Tier 2 migration (`[Obsolete]` recommendations + internal rewrite to delegate to the new builder) is a focused fast-follow release once v1 has stabilized.
15. **Migration: no hard breaks anywhere.** Including in alpha packages. When Tier 2 lands, existing extensions deprecate with at least one release of overlap before any removal is considered.

## Package layout

### v1 NuGet packages (new)

```
Microsoft.Agents.AI.Hosting.Channels                 (core)
├── AgentFrameworkHost
├── IAgentFrameworkHostBuilder
├── HostApplicationBuilderHostingChannelsExtensions  (AddAgentFrameworkHost on IHostApplicationBuilder)
├── EndpointRouteBuilderHostingChannelsExtensions    (MapAgentFrameworkHost on IEndpointRouteBuilder)
├── Channel                                          (abstract class)
├── ChannelContribution                              (record, init-only)
├── ChannelRequest                                   (record; full Python parity)
├── ChannelSession                                   (record; all fields nullable)
├── SessionMode                                      (enum: Auto / Required / Disabled)
├── ChannelIdentity
├── ChannelCommand
├── ResponseTarget                                   (sealed abstract record + nested cases)
├── HostedRunResult                                  (non-generic base)
├── HostedRunResult<TResult>                         (generic envelope)
├── HostedStreamItem                                 (envelope IAsyncEnumerable<T> wraps)
├── IChannelContext                                  (handed to Contribute)
├── ConversationScope                                (enum: PerUser / PerUserPerConversation / PerConversation)
├── AcceptInGroup                                    (enum: MentionOnly / CommandOnly / MentionOrCommand / All)
├── IChannelPush                                     (capability)
├── ChannelPushContext                               (per-delivery context)
├── IChannelPushCodec                                (capability)
├── IChannelRunHook
├── IChannelResponseHook
├── ChannelResponseContext
├── IChannelStreamTransformHook
├── IConfidentialityTagged                           (link policy tier)
├── IHostedTargetRunner                              (seam)
│   ├── AIAgentRunner                                (built in)
│   └── WorkflowRunner                               (built in)
├── IIdentityAllowlist                               (Allow / Deny / Abstain tri-state)
├── IIdentityLinker
├── AuthorizationContext                             (phase, identity, claims, source)
├── AuthorizationOutcome                             (Allowed / LinkRequired / Denied)
├── AuthorizationPhase                               (enum: PreLink / PostLink)
├── ClaimSource                                      (enum: None / Channel / Linker)
├── AllowlistDecision                                (enum)
├── AuthorizationProfile                             (factory: Open / ForcedLink / NativeAllowlist / LinkedClaimAllowlist / Mixed)
├── AllowAllIdentityAllowlist
├── NativeIdAllowlist
├── LinkedClaimAllowlist
├── AnyOfIdentityAllowlist                           (combinator)
├── AllOfIdentityAllowlist                           (combinator)
├── CallableIdentityAllowlist                        (escape hatch)
├── LinkChallenge
├── LinkedIdentity
├── PrincipalIdentity                                (linker result; verified claims + native id)
├── OneTimeCodeIdentityLinker                        (zero-dep built-in)
├── ILinkPolicy                                      (decides which channels may share an isolation key / deliver to one another)
├── LinkPolicyContext                                (Source / Destination / Operation)
├── AllowAllLinkPolicy
├── SameConfidentialityTierLinkPolicy
├── ExplicitAllowListLinkPolicy
├── DenyAllLinkPolicy
├── IHostStateStore                                  (identity registry + link grants + last-seen + continuations + session reset)
├── ChannelIdentityRegistration                      (record persisted by the store)
├── LinkGrant                                        (record persisted by the store)
├── LastSeenRecord                                   (record persisted by the store)
├── ContinuationToken                                (record; status + result + isolation key)
├── InMemoryHostStateStore
├── FileHostStateStore
├── HostStatePathOptions                             (Root / RunnerPath / LinksPath / ContinuationsPath / LastSeenPath)
├── IDurableTaskRunner                               (Register / Schedule / Get / Cancel)
├── TaskHandle                                       (record; opaque task id)
├── DurableTaskPayloadMode                           (enum: Object / Json)
├── RetryPolicy                                      (record)
├── InProcessDurableTaskRunner                       (IHostedService + bounded Channel<T>)
├── IsolationKeys                                    (record + static AsyncLocal slot)
├── IIsolationKeysAccessor                           (DI wrapper)
└── IsolationKeysMiddleware                          (lifts x-agent-*-isolation-key headers)

Microsoft.Agents.AI.Hosting.Channels.Responses
├── ResponsesChannel
├── ResponsesChannelOptions                          (Path / RunHook / ExposeConversations / Transports)
└── AgentFrameworkHostBuilderResponsesExtensions     (AddResponsesChannel)

Microsoft.Agents.AI.Hosting.Channels.Invocations
├── InvocationsChannel
├── InvocationsChannelOptions                        (Path / RunHook / OpenApiSpec)
├── WorkflowInvocationsResponseHook                  (RequestInfoEvent envelope)
└── AgentFrameworkHostBuilderInvocationsExtensions   (AddInvocationsChannel)

Microsoft.Agents.AI.Hosting.Channels.Telegram
├── TelegramChannel                                  (uses Telegram.Bot)
├── TelegramChannelOptions                           (BotToken / Transport / Path / ConversationScope / AcceptInGroup / RequireLink / Commands / RegisterNativeCommands)
└── AgentFrameworkHostBuilderTelegramExtensions      (AddTelegramChannel)
```

### v1 NuGet packages (additive change to existing)

```
Microsoft.Agents.AI.Foundry.Hosting                  (untouched existing surface)
└── FoundryHostedAgentRunner : IHostedTargetRunner   (NEW — additive)
```

### v1 NuGet packages (untouched)

```
Microsoft.Agents.AI.Hosting.OpenAI                   (unchanged — keeps MapOpenAIResponses)
Microsoft.Agents.AI.Hosting.A2A                      (unchanged)
Microsoft.Agents.AI.Hosting.A2A.AspNetCore           (unchanged)
Microsoft.Agents.AI.Hosting.AGUI.AspNetCore          (unchanged)
Microsoft.Agents.AI.Hosting.AzureFunctions           (unchanged)
```

### Fast-follow packages (post-v1)

```
Microsoft.Agents.AI.Hosting.Channels.DurableTask     (DurableTaskFrameworkRunner wrapping existing DTF integration)
Microsoft.Agents.AI.Hosting.Channels.Discord         (mirrors Python PR #6081)
Microsoft.Agents.AI.Hosting.Channels.Activity        (Teams / DirectLine / WebChat via Activity Protocol)
Microsoft.Agents.AI.Hosting.Channels.EntraId         (EntraIdentityLinker)
```

## API changes

> All signatures below are draft; final names, nullability annotations, and `Experimental` attributes get sharpened during implementation. The shape and ergonomics are what reviewers should evaluate. Every public type ships with the standard copyright header and is annotated `[Experimental(DiagnosticIds.Experiments.<id>)]` for the v1 release.

> **Namespace convention.** Public types live in `Microsoft.Agents.AI.Hosting.Channels` (and `*.Responses`, `*.Invocations`, `*.Telegram`). Extension methods follow the repo convention: `IHostApplicationBuilder` extensions live in `namespace Microsoft.Extensions.Hosting`; `IEndpointRouteBuilder` extensions live in `namespace Microsoft.AspNetCore.Builder`; `IServiceCollection` extensions live in `namespace Microsoft.Extensions.DependencyInjection`. Channel-add extensions on `IAgentFrameworkHostBuilder` live in `Microsoft.Agents.AI.Hosting.Channels`.

### Host + builder

```csharp
namespace Microsoft.Agents.AI.Hosting.Channels;

public sealed class AgentFrameworkHost
{
    internal AgentFrameworkHost(IServiceProvider services);

    public IServiceProvider Services { get; }
    public IReadOnlyList<Channel> Channels { get; }
    public IHostedTargetRunner TargetRunner { get; }

    public ValueTask<ContinuationToken> RunInBackgroundAsync(
        ChannelRequest request,
        CancellationToken cancellationToken = default);

    public ValueTask<ContinuationToken?> GetContinuationAsync(
        string token,
        CancellationToken cancellationToken = default);

    public ValueTask ResetSessionAsync(
        string isolationKey,
        CancellationToken cancellationToken = default);

    public ValueTask<AuthorizationOutcome> AuthorizeAsync(
        ChannelIdentity identity,
        AuthorizationRequest options,
        CancellationToken cancellationToken = default);
}

public sealed record AuthorizationRequest
{
    public bool RequireLink { get; init; }
    public IIdentityAllowlist? Allowlist { get; init; }
    public IReadOnlyDictionary<string, string>? VerifiedClaims { get; init; }
    public ConversationContext? ConversationContext { get; init; }
}
```

```csharp
namespace Microsoft.Extensions.Hosting;

public static class HostApplicationBuilderHostingChannelsExtensions
{
    // The three primary target overloads mirror Python's HostableTarget union.
    public static IAgentFrameworkHostBuilder AddAgentFrameworkHost(
        this IHostApplicationBuilder builder,
        AIAgent target,
        Action<AgentFrameworkHostOptions>? configure = null);

    public static IAgentFrameworkHostBuilder AddAgentFrameworkHost(
        this IHostApplicationBuilder builder,
        Workflow target,
        Action<AgentFrameworkHostOptions>? configure = null);

    // Keyed overload aligns with existing AddAIAgent(key, ...) ergonomics.
    public static IAgentFrameworkHostBuilder AddAgentFrameworkHost(
        this IHostApplicationBuilder builder,
        string agentKey,
        Action<AgentFrameworkHostOptions>? configure = null);

    // Factory overload — host resolves the target lazily so the runner can be replaced from DI.
    public static IAgentFrameworkHostBuilder AddAgentFrameworkHost<TTarget>(
        this IHostApplicationBuilder builder,
        Func<IServiceProvider, TTarget> targetFactory,
        Action<AgentFrameworkHostOptions>? configure = null)
        where TTarget : class;
}

public sealed record AgentFrameworkHostOptions
{
    public IIdentityAllowlist? DefaultAllowlist { get; init; }
    public ILinkPolicy? LinkPolicy { get; init; }
    public HostStatePathOptions? StatePaths { get; init; }
    public string? DefaultDurableRunnerName { get; init; }
    public bool AllowInProcessRunnerInEphemeralMode { get; init; }
}
```

```csharp
namespace Microsoft.AspNetCore.Builder;

public static class EndpointRouteBuilderHostingChannelsExtensions
{
    // Returns IEndpointConventionBuilder so authors can attach .RequireAuthorization() etc. on
    // every host-owned endpoint at once (e.g. all per-channel route groups).
    public static IEndpointConventionBuilder MapAgentFrameworkHost(
        this IEndpointRouteBuilder endpoints);
}
```

```csharp
namespace Microsoft.Agents.AI.Hosting.Channels;

public interface IAgentFrameworkHostBuilder
{
    IServiceCollection Services { get; }
    AgentFrameworkHostOptions Options { get; }

    // Generic AddChannel + per-channel-package extension methods (AddResponsesChannel, etc.).
    IAgentFrameworkHostBuilder AddChannel(Channel channel);
    IAgentFrameworkHostBuilder AddChannel<TChannel>(Func<IServiceProvider, TChannel> factory)
        where TChannel : Channel;

    // Replace the default identity linker / allowlist / link policy registration.
    IAgentFrameworkHostBuilder UseIdentityLinker<TLinker>() where TLinker : class, IIdentityLinker;
    IAgentFrameworkHostBuilder UseDefaultAllowlist(IIdentityAllowlist allowlist);
    IAgentFrameworkHostBuilder UseLinkPolicy(ILinkPolicy policy);

    // Replace the default durable runner / host state store registration.
    IAgentFrameworkHostBuilder UseDurableTaskRunner<TRunner>() where TRunner : class, IDurableTaskRunner;
    IAgentFrameworkHostBuilder UseHostStateStore<TStore>() where TStore : class, IHostStateStore;
}
```

### Channel contract

```csharp
public abstract class Channel
{
    public abstract string Name { get; }

    // The mount root for this channel's routes. The host wraps Routes in `endpoints.MapGroup(Path)`
    // before invoking each action, so route actions should map paths relative to Path.
    // Path = "" mounts at the host's own root.
    public virtual string Path => string.Empty;

    // Runs at AddChannel time (pre-Build). Channels register their own DI services here.
    public virtual void ConfigureServices(IServiceCollection services) { }

    // Runs at MapAgentFrameworkHost time (post-Build).
    public abstract ChannelContribution Contribute(IChannelContext context);
}

public sealed record ChannelContribution
{
    // Each action is invoked with a group builder rooted at Channel.Path.
    public IReadOnlyList<Action<IEndpointRouteBuilder>> Routes { get; init; } = [];

    // Endpoint filters applied to the Path-rooted group (replaces Python's `middleware`).
    public IReadOnlyList<IEndpointFilter> EndpointFilters { get; init; } = [];

    public IReadOnlyList<ChannelCommand> Commands { get; init; } = [];
    public Func<CancellationToken, ValueTask>? OnStartup { get; init; }
    public Func<CancellationToken, ValueTask>? OnShutdown { get; init; }
}

public interface IChannelContext
{
    IServiceProvider Services { get; }
    IHostStateStore StateStore { get; }
    IDurableTaskRunner DurableRunner { get; }

    // Authorization is owned by the host. Channels call this after extracting ChannelIdentity
    // and any natively verified claims; the result is an AuthorizationOutcome the channel
    // projects onto its protocol (200 / 403 / link-required envelope).
    ValueTask<AuthorizationOutcome> AuthorizeAsync(
        ChannelIdentity identity,
        AuthorizationRequest options,
        CancellationToken cancellationToken);

    // The non-generic host run/stream entry. Workflow-friendly base type for results.
    ValueTask<HostedRunResult> RunAsync(ChannelRequest request, CancellationToken cancellationToken);

    // Streaming yields HostedStreamItem envelopes (HostedStreamUpdate / HostedStreamEvent /
    // HostedStreamCompleted), so the host can surface both typed agent updates and protocol-
    // specific events (workflow RequestInfoEvent, AG-UI StateSnapshotEvent, ...) behind one
    // stream type.
    IAsyncEnumerable<HostedStreamItem> StreamAsync(ChannelRequest request, CancellationToken cancellationToken);

    // Schedules outbound delivery for non-originating destinations via the durable runner.
    // Resolves the destination set (Originating / Active / Channel / ...) against the configured
    // LinkPolicy + IHostStateStore, then enqueues one `hosting.push` task per non-originating
    // destination. Originating delivery is NOT scheduled here — channels render their own
    // originating reply synchronously. Returns one TaskHandle per scheduled push.
    ValueTask<IReadOnlyList<TaskHandle>> ScheduleResponseAsync(
        HostedRunResult result,
        ChannelRequest originating,
        CancellationToken cancellationToken);
}
```

> **Hook discovery and host-managed extensibility.** Built-in channels implement the relevant capability interfaces themselves and pull app-supplied behavior off their options (`ResponsesChannelOptions.RunHook`, `TelegramChannelOptions.ResponseHook`, etc.). The host discovers capabilities by checking `channel is IChannelPush`, `channel is IChannelResponseHook`, etc. Third-party channel authors follow the same pattern: implement the capability on the channel class itself and route app-configurable concerns through the channel's options record.

### Channel-neutral request envelope

```csharp
public sealed record ChannelRequest
{
    // Originating channel name (matches Channel.Name).
    public required string Channel { get; init; }

    // Operation kind: "message.create", "command.invoke", "approval.respond", ...
    public required string Operation { get; init; }

    // Reuses framework input types. Boxed as object because the union spans AIAgentRunInput,
    // ChatMessage[], a workflow-typed input, etc.
    public required object Input { get; init; }

    // Session hint from the channel. Nullable: caller-supplied channels populate it from the
    // wire; host-tracked channels leave it null and let the host per-isolation-key alias decide.
    public ChannelSession? Session { get; init; }

    // Channel-native USER identity observed on this request (never the chat / conversation id).
    public ChannelIdentity? Identity { get; init; }

    // Protocol-visible conversation/thread identifier when one exists. In multi-user surfaces
    // (Telegram groups, Teams team channels) this differs from Identity.NativeId.
    public string? ConversationId { get; init; }

    // Caller-derived chat options forwarded onto ChatOptions used by the target runner. Reuses
    // Microsoft.Extensions.AI.ChatOptions so chat-client knobs (temperature, top_p, response_format,
    // tool choice, additional properties) pass through without translation.
    public ChatOptions? Options { get; init; }

    // Whether host-managed session use is automatic, mandatory, or bypassed.
    public SessionMode SessionMode { get; init; } = SessionMode.Auto;

    // Protocol-level metadata for telemetry. Host code never reads this; reserved for channel
    // private bookkeeping.
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = ImmutableDictionary<string, object?>.Empty;

    // Channel-specific structured values surfaced to the run hook (signature state, capability
    // hints, deployment-specific knobs parsed off `extra_body`). Two reserved keys for workflow
    // targets: "workflow.checkpoint_id" and "workflow.resume_token" (see "Workflow channels").
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = ImmutableDictionary<string, object?>.Empty;

    // Bidirectional, mutable per-request state slot for event-rich front-ends (AG-UI).
    // Opaque to the host; channels thread it through a channel-owned ContextProvider.
    public IDictionary<string, object?>? ClientState { get; init; }

    // Frontend tool catalog supplied per request. Forwarded onto ChatOptions but tool execution
    // returns to the client (host never invokes them).
    public IReadOnlyList<AITool>? ClientTools { get; init; }

    // Pass-through bag for channel-protocol extras the run hook needs to route into the target
    // (e.g. AG-UI `resume` / `command` / HITL response payloads). Opaque to the host.
    public IReadOnlyDictionary<string, object?>? ForwardedProps { get; init; }

    // Whether to invoke StreamAsync rather than RunAsync.
    public bool Stream { get; init; }

    // Where the response is delivered. Defaults to ResponseTarget.Originating.
    public ResponseTarget? ResponseTarget { get; init; }

    // If true, host returns a ContinuationToken immediately rather than awaiting the response.
    // Forced true when ResponseTarget is ResponseTarget.None.
    public bool Background { get; init; }
}

public sealed record ChannelSession
{
    // Stable host lookup key for an AgentSession. Caller-supplied channels populate from the
    // wire (previous_response_id, etc.). Host-tracked channels leave null.
    public string? Key { get; init; }

    public string? ConversationId { get; init; }

    // Opaque isolation boundary (user, tenant, chat, ...) using hosted-agent terminology.
    public string? IsolationKey { get; init; }

    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = ImmutableDictionary<string, object?>.Empty;
}

public sealed record ChannelIdentity(string Channel, string NativeId)
{
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = ImmutableDictionary<string, string>.Empty;
}

public enum SessionMode { Auto, Required, Disabled }

// Non-generic base lets channels and the host operate against HostedRunResult without committing
// to a TResult; generic subclass preserves full-fidelity typed access.
public abstract record HostedRunResult
{
    public ChannelSession? Session { get; init; }
    public abstract object? ResultObject { get; }
}

public sealed record HostedRunResult<TResult> : HostedRunResult
{
    public required TResult Result { get; init; }
    public override object? ResultObject => Result;

    // Shallow clone with a rewritten Result (per-destination response-hook rebinding).
    public HostedRunResult<TNew> Replace<TNew>(TNew newResult) =>
        new() { Result = newResult, Session = Session };
}

// One item produced by IChannelContext.StreamAsync — covers both agent updates and workflow events.
// HostedStreamUpdate wraps the normalized agent stream (lossless for messages, function calls,
// usage). HostedStreamEvent passes through protocol-specific events the framework does not model
// (workflow RequestInfoEvent, AG-UI StateSnapshotEvent, ToolCallStartEvent). HostedStreamCompleted
// is always the terminal item and carries the final HostedRunResult for downstream bookkeeping
// (intended_targets envelope, durable push scheduling).
public abstract record HostedStreamItem;
public sealed record HostedStreamUpdate(AgentRunResponseUpdate Update) : HostedStreamItem;
public sealed record HostedStreamEvent(object Event) : HostedStreamItem;
public sealed record HostedStreamCompleted(HostedRunResult Result) : HostedStreamItem;
```

### Response target

`ResponseTarget` directs where the host delivers the agent response. Independent of `SessionMode`. Mirrors Python's `ResponseTarget` factory + variants.

```csharp
public abstract record ResponseTarget
{
    public static readonly ResponseTarget Originating = new OriginatingResponseTarget();
    public static readonly ResponseTarget Active      = new ActiveResponseTarget();
    public static readonly ResponseTarget AllLinked   = new AllLinkedResponseTarget();
    public static readonly ResponseTarget None        = new NoneResponseTarget();

    public static ResponseTarget Channel(string channelName, bool echoInput = false)
        => new ChannelResponseTarget(channelName, echoInput);
    public static ResponseTarget Channels(IReadOnlyList<string> channelNames, bool echoInput = false)
        => new ChannelsResponseTarget(channelNames, echoInput);
    public static ResponseTarget Identities(IReadOnlyList<ChannelIdentity> identities, bool echoInput = false)
        => new IdentitiesResponseTarget(identities, echoInput);
    public static ResponseTarget Identity(ChannelIdentity identity, bool echoInput = false)
        => new IdentitiesResponseTarget([identity], echoInput);

    public sealed record OriginatingResponseTarget : ResponseTarget;
    public sealed record ActiveResponseTarget      : ResponseTarget;
    public sealed record AllLinkedResponseTarget   : ResponseTarget;
    public sealed record NoneResponseTarget        : ResponseTarget;
    public sealed record ChannelResponseTarget(string ChannelName, bool EchoInput) : ResponseTarget;
    public sealed record ChannelsResponseTarget(IReadOnlyList<string> ChannelNames, bool EchoInput) : ResponseTarget;
    public sealed record IdentitiesResponseTarget(IReadOnlyList<ChannelIdentity> Identities, bool EchoInput) : ResponseTarget;
}
```

**Fallback rules** (mirror Python):

- When a destination channel does not implement `IChannelPush`, that destination is dropped and a warning is surfaced in telemetry; if the resolved set is empty, the host falls back to `Originating`.
- `LinkPolicy` is consulted for every destination; policy-dropped destinations are recorded in the assistant message's `intended_targets` envelope as `skipped_targets[].reason = "link_policy"`.
- `ResponseTarget.None` forces `Background = true` and returns a `ContinuationToken` on the originating wire.
- `EchoInput` causes the host to bundle a `role="user"` echo push and the agent reply into the same scheduled push task per non-originating destination; the runner tracks `echo_done` so a retry after the echo succeeded does not double-echo.

### Capability interfaces

```csharp
public interface IChannelPush
{
    ValueTask PushAsync(
        ChannelPushContext context,
        HostedRunResult payload,
        CancellationToken cancellationToken);
}

public sealed record ChannelPushContext
{
    public required ChannelIdentity Destination { get; init; }
    public required ChannelRequest OriginatingRequest { get; init; }
    public required string OriginatingChannel { get; init; }
    public bool IsEcho { get; init; }
    public ResponseTarget? OriginalTarget { get; init; }
}

public interface IChannelPushCodec
{
    // Encode the whole push envelope so out-of-process runners (JSON payload mode) can reconstruct
    // the destination identity, originating request, echo flag, and result on the worker side.
    JsonNode Encode(ChannelPushContext context, HostedRunResult payload);
    (ChannelPushContext Context, HostedRunResult Payload) Decode(JsonNode encoded);
}

public interface IChannelRunHook
{
    // Runs AFTER the channel produces its default ChannelRequest and BEFORE the host resolves
    // session behavior and calls the target. Canonical adapter point for workflow targets.
    ValueTask<ChannelRequest> OnRequestAsync(
        ChannelRequest request,
        ChannelRunHookContext context,
        CancellationToken cancellationToken);
}

public sealed record ChannelRunHookContext
{
    public required object Target { get; init; }            // AIAgent or Workflow
    public object? ProtocolRequest { get; init; }           // raw inbound payload, loosely typed
}

public interface IChannelResponseHook
{
    // Receives a per-destination clone of HostedRunResult and returns a (possibly rewritten)
    // replacement. Hooks rebind via `result.Replace(...)` rather than mutating in place.
    ValueTask<HostedRunResult> OnResponseAsync(
        HostedRunResult result,
        ChannelResponseContext context,
        CancellationToken cancellationToken);
}

public sealed record ChannelResponseContext
{
    public required ChannelRequest Request { get; init; }
    public required string ChannelName { get; init; }
    public required ChannelIdentity DestinationIdentity { get; init; }
    public bool Originating { get; init; }
    public bool IsEcho { get; init; }
}

public interface IChannelStreamTransformHook
{
    IAsyncEnumerable<AgentRunResponseUpdate> Transform(
        IAsyncEnumerable<AgentRunResponseUpdate> upstream,
        CancellationToken cancellationToken);
}

public interface IConfidentialityTagged
{
    string? ConfidentialityTier { get; }   // opaque label; null = single-tier
}
```

### Identity stack

The host owns the authorization pipeline. Channels never run allowlists themselves — they call `host.AuthorizeAsync(...)` after extracting `ChannelIdentity` and any natively verified claims.

```csharp
public enum AllowlistDecision { Allow, Deny, Abstain }
public enum AuthorizationPhase { PreLink, PostLink }
public enum ClaimSource { None, Channel, Linker }

public interface IIdentityAllowlist
{
    // If true, the host startup validator rejects configurations where neither RequireLink=true
    // nor a claim-emitting channel can deliver the claims this allowlist needs. Prevents the
    // silent-deny-everyone footgun.
    bool RequiresLinkedClaims => false;

    ValueTask<AllowlistDecision> EvaluateAsync(
        AuthorizationContext context,
        CancellationToken cancellationToken);
}

public sealed record AuthorizationContext
{
    public required ChannelIdentity Identity { get; init; }
    public required AuthorizationPhase Phase { get; init; }
    public string? IsolationKey { get; init; }                 // null at PreLink; resolved at PostLink
    public IReadOnlyDictionary<string, string> VerifiedClaims { get; init; }
        = ImmutableDictionary<string, string>.Empty;
    public ClaimSource ClaimSource { get; init; } = ClaimSource.None;
    public ConversationContext? ConversationContext { get; init; }
}

public sealed record ConversationContext(string? ConversationId, bool IsGroup);

// Discriminated outcome of host.AuthorizeAsync(...).
public abstract record AuthorizationOutcome
{
    public sealed record Allowed(string IsolationKey) : AuthorizationOutcome;

    public sealed record LinkRequired(LinkChallenge Challenge) : AuthorizationOutcome;

    public sealed record Denied(
        string ReasonCode,                                        // stable machine-readable
        string? UserMessage = null,                               // safe to render publicly
        IReadOnlyDictionary<string, object?>? LogDetails = null   // never shown to users
    ) : AuthorizationOutcome;
}

public interface IIdentityLinker
{
    string Name { get; }

    // Same shape as Channel.Contribute — lets the linker publish callback/verification routes.
    ChannelContribution Contribute(IChannelContext context);

    ValueTask<LinkChallenge> BeginAsync(
        ChannelIdentity identity,
        string? requestedIsolationKey,
        CancellationToken cancellationToken);

    ValueTask<string> CompleteAsync(
        string challengeId,
        IReadOnlyDictionary<string, object?> proof,
        CancellationToken cancellationToken);

    // Returns the isolation key for an already-linked identity, or null if no link exists.
    // When verifiedClaims contains entries that already match in the link store, the linker
    // silently auto-merges the (channel, native_id) onto the existing isolation key and returns it.
    ValueTask<string?> IsLinkedAsync(
        ChannelIdentity identity,
        IReadOnlyDictionary<string, string>? verifiedClaims,
        CancellationToken cancellationToken);
}

public sealed record PrincipalIdentity(
    string IsolationKey,
    ChannelIdentity Identity,
    IReadOnlyDictionary<string, string> VerifiedClaims);

public sealed record LinkChallenge(
    string ChallengeId,
    string Kind,                                              // "url", "code", "mfa"
    Uri? Url = null,
    string? Code = null,
    string? UserPrompt = null);

// Built-in allowlist constructors return IIdentityAllowlist instances.
public static class AuthorizationProfile
{
    // require_link=false, allowlist=AllowAll. Every identity gets an auto-issued isolation key.
    public static IIdentityAllowlist Open();

    // require_link=true, allowlist=AllowAll. Any successfully-linked identity is admitted.
    public static IIdentityAllowlist ForcedLink();

    // require_link=false, NativeIdAllowlist(channel, ids). Pre-link, no IdP claim involved.
    public static IIdentityAllowlist NativeAllowlist(string channel, params string[] nativeIds);

    // require_link=true, LinkedClaimAllowlist(claim, values). Forces link ceremony.
    public static IIdentityAllowlist LinkedClaimAllowlist(string claim, params string[] values);

    // require_link=false, AnyOf(NativeIdAllowlist, LinkedClaimAllowlist). Native ids bypass link;
    // everyone else funnels into it.
    public static IIdentityAllowlist Mixed(
        IIdentityAllowlist nativeAllowlist,
        IIdentityAllowlist linkedClaimAllowlist);
}

// Combinators
public sealed class AnyOfIdentityAllowlist(params IIdentityAllowlist[] children) : IIdentityAllowlist { /* ... */ }
public sealed class AllOfIdentityAllowlist(params IIdentityAllowlist[] children) : IIdentityAllowlist { /* ... */ }
```

**Authorization decision pipeline** (mirror Python). The host runs this inside `AuthorizeAsync(...)`:

1. Build `AuthorizationContext(Phase = PreLink, VerifiedClaims = ..., ClaimSource = ...)`.
2. `pre = allowlist.EvaluateAsync(ctx)` — defaults to `Allow` when `allowlist is null`.
3. `pre == Deny` → `Denied(reasonCode: "allowlist_denied_pre_link", ...)`.
4. `pre == Allow`:
   - If `RequireLink == true` and the linker has no record yet → `LinkRequired(linker.BeginAsync(identity))`.
   - Otherwise → `Allowed(resolved-or-auto-issued isolation key)`.
5. `pre == Abstain`:
   - If `RequireLink == true` **or** the allowlist declared `RequiresLinkedClaims` → call `linker.IsLinkedAsync(identity, verifiedClaims)`.
     - Not linked → `LinkRequired(linker.BeginAsync(identity))`.
     - Linked → re-evaluate at `Phase = PostLink` with linker-emitted claims.
       - `Allow` → `Allowed(linked isolation key)`.
       - `Deny` → `Denied(reasonCode: "allowlist_denied_post_link", ...)`.
       - `Abstain` post-link is a misconfiguration; logged and treated as `Denied(reasonCode: "allowlist_abstain_after_link")`.
   - Otherwise → `Allowed(auto-issued isolation key)`.

**Default-open and all-abstain semantics.** With zero allowlists registered (or `allowlist: null`), every request is `Allowed` and auto-issues an isolation key keyed on `(Channel, NativeId)`. An all-abstain outcome at `PreLink` is treated as `Allow` when no `RequireLink` is set; at `PostLink` it is a misconfiguration as described above.

**Inheritance.** Channel `allowlist` parameter has three states: `Inherit` (the host `DefaultAllowlist` applies), explicitly null (the channel is open even when the host has a default), or an explicit `IIdentityAllowlist` (overrides the host default; combine via `AllOfIdentityAllowlist(host.DefaultAllowlist, MyExtraList)` to add to it rather than replace).

**Startup validation (fail-fast).** `AgentFrameworkHost` runs a validator at `MapAgentFrameworkHost(...)` startup:

1. If any channel's resolved allowlist contains a node with `RequiresLinkedClaims == true`, the channel must either set `RequireLink = true` or declare via `Channel.EmitsVerifiedClaims = true` that it natively emits verified claims (e.g. an `ActivityChannel` carrying AAD `oid` on the inbound bearer). Otherwise: throw `ChannelConfigurationException`.
2. If any resolved allowlist contains `LinkedClaimAllowlist` and the host has no `IIdentityLinker` registered: throw `ChannelConfigurationException`.
3. If any channel has `RequireLink = true` and no `IIdentityLinker` is registered: throw `ChannelConfigurationException`.
4. `NativeIdAllowlist(channel: <other>)` referencing an unknown channel: throw `ChannelConfigurationException`.

Eager startup failure is intentional — silent deny-everyone is the worst possible default.

### Link policy and confidentiality tier

`ILinkPolicy` decides which channels may share an `IsolationKey` (consulted by `IIdentityLinker` on link attempts) and which channels may be a `ResponseTarget` for one another (consulted by the host's response-routing layer on every delivery).

```csharp
public interface ILinkPolicy
{
    ValueTask<bool> EvaluateAsync(LinkPolicyContext context, CancellationToken cancellationToken);
}

public sealed record LinkPolicyContext
{
    public required Channel Source { get; init; }
    public required Channel Destination { get; init; }
    public required LinkPolicyOperation Operation { get; init; }   // Link or Deliver
}

public enum LinkPolicyOperation { Link, Deliver }

// Built-in policies
public sealed class AllowAllLinkPolicy : ILinkPolicy;                                            // default
public sealed class SameConfidentialityTierLinkPolicy : ILinkPolicy;                             // most common
public sealed class ExplicitAllowListLinkPolicy(IReadOnlyList<(string Source, string Destination)> AllowedPairs) : ILinkPolicy;
public sealed class DenyAllLinkPolicy : ILinkPolicy;                                             // share target, never sessions
```

Refusal during `Link` raises a typed error to the user. Refusal during `Deliver` excludes that destination from the route set and falls back to `Originating` if the route set becomes empty.

### Host state store

`IHostStateStore` is the single persistence seam for **host-execution metadata** that outlives a single request: continuation tokens, identity registry, identity-link grants, and last-seen `(IsolationKey, Channel)` records. Separate from `AgentSessionStore` (per-conversation history) and `WorkflowBuilder.CheckpointStorage` (workflow checkpoints).

```csharp
public interface IHostStateStore
{
    // ---- Identity registry: (channel, native_id) <-> isolation_key with atomic merge ----

    ValueTask<string?> GetIsolationKeyAsync(
        ChannelIdentity identity, CancellationToken cancellationToken);

    // Atomically registers (or merges) a channel-native identity onto an isolation key. If the
    // identity is already mapped to a different isolation key, both keys' (channel, native_id)
    // records are merged onto the requested key. Optional verifiedClaims are persisted alongside
    // so future channels presenting the same claim auto-link without a second ceremony.
    ValueTask SaveLinkAsync(
        ChannelIdentity identity,
        string isolationKey,
        IReadOnlyDictionary<string, string>? verifiedClaims,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<ChannelIdentityRegistration>> GetIdentitiesAsync(
        string isolationKey, CancellationToken cancellationToken);

    ValueTask<string?> LookupByVerifiedClaimAsync(
        string claim, string value, CancellationToken cancellationToken);

    // ---- Link grants (Entra OAuth state, one-time codes) ----

    ValueTask SaveLinkGrantAsync(LinkGrant grant, CancellationToken cancellationToken);
    ValueTask<LinkGrant?> GetLinkGrantAsync(string code, CancellationToken cancellationToken);
    ValueTask<LinkGrant?> ConsumeLinkGrantAsync(string code, CancellationToken cancellationToken);

    // ---- Last-seen ledger backing ResponseTarget.Active ----

    ValueTask RecordLastSeenAsync(
        string isolationKey,
        ChannelIdentity identity,
        string? conversationId,
        DateTimeOffset at,
        CancellationToken cancellationToken);

    ValueTask<LastSeenRecord?> GetLastSeenAsync(
        string isolationKey, CancellationToken cancellationToken);

    // ---- Continuation tokens (background runs) ----

    ValueTask SaveContinuationAsync(ContinuationToken token, CancellationToken cancellationToken);
    ValueTask<ContinuationToken?> GetContinuationAsync(string token, CancellationToken cancellationToken);
    ValueTask DeleteContinuationAsync(string token, CancellationToken cancellationToken);

    // ---- Session alias rotation (backs host.ResetSessionAsync for host-tracked channels) ----

    ValueTask RotateSessionAliasAsync(string isolationKey, CancellationToken cancellationToken);
    ValueTask<string?> GetActiveSessionAliasAsync(string isolationKey, CancellationToken cancellationToken);
}

public sealed record ChannelIdentityRegistration(
    ChannelIdentity Identity,
    DateTimeOffset RegisteredAt,
    IReadOnlyDictionary<string, string> VerifiedClaims);

public sealed record LinkGrant(
    string Code,
    string IssuedByLinker,
    string? RequestedIsolationKey,
    DateTimeOffset ExpiresAt,
    IReadOnlyDictionary<string, object?> Payload);

public sealed record LastSeenRecord(
    ChannelIdentity Identity,
    string? ConversationId,
    DateTimeOffset At);

public sealed record ContinuationToken
{
    public required string Token { get; init; }
    public required ContinuationStatus Status { get; init; }
    public string? IsolationKey { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public HostedRunResult? Result { get; init; }
    public string? Error { get; init; }
    public ResponseTarget? ResponseTarget { get; init; }
}

public enum ContinuationStatus { Queued, Running, Completed, Failed }

public sealed record HostStatePathOptions
{
    public string? Root { get; init; }            // shorthand: derives all subpaths if not set
    public string? RunnerPath { get; init; }      // in-process durable runner persistence
    public string? LinksPath { get; init; }       // identity registry + link grants
    public string? ContinuationsPath { get; init; }
    public string? LastSeenPath { get; init; }
}
```

> **Workflow checkpoint storage is not an `IHostStateStore` concern.** Per decision 13, workflow checkpoints stay on `WorkflowBuilder.CheckpointStorage`. There is no `CheckpointsPath` on `HostStatePathOptions`.

> **Default selection by runtime mode.** Pure worker / dev (`HostingMode.LongRunning` per Python parlance): `FileHostStateStore` keyed off `HostStatePathOptions.Root` (defaults to `./.afhost/`). ASP.NET web app: same default. `HostingMode.Ephemeral` (Foundry hosted-agent runtime, scale-to-zero): caller must wire an external store (Cosmos, SQL, Redis); falling back to in-memory is rejected at startup unless `AgentFrameworkHostOptions.AllowInProcessRunnerInEphemeralMode = true`. In v1 only `InMemoryHostStateStore` and `FileHostStateStore` ship in core; external implementations land in fast-follow per req #24.

### Durable task runner

The host delegates non-originating push fan-out and background runs to a pluggable `IDurableTaskRunner`. Channels never see it directly; they emit `IChannelPush.PushAsync(...)` and the runner schedules + retries.

```csharp
public interface IDurableTaskRunner
{
    // Each runner implementation declares its payload mode. Json-mode runners (out-of-process
    // sidecars, gRPC TaskHub) require channels with non-JSON payloads to expose an IChannelPushCodec.
    DurableTaskPayloadMode PayloadMode { get; }

    // Registers a handler under a name. The host registers "hosting.push" at startup; channel
    // authors typically do not register their own handlers.
    void Register(string name, Func<TaskInvocationContext, ValueTask> handler);

    ValueTask<TaskHandle> ScheduleAsync(
        string name,
        object payload,
        RetryPolicy? retryPolicy,
        CancellationToken cancellationToken);

    ValueTask<TaskStatus?> GetAsync(TaskHandle handle, CancellationToken cancellationToken);
    ValueTask CancelAsync(TaskHandle handle, CancellationToken cancellationToken);
}

public enum DurableTaskPayloadMode { Object, Json }

public sealed record TaskHandle(string TaskId, string Name);

public enum TaskStatus { Scheduled, Running, Succeeded, Failed, Cancelled }

public sealed record TaskInvocationContext(
    string Name,
    object Payload,
    int Attempt,
    IDictionary<string, object?> State);   // mutable runner-owned per-task state (e.g. echo_done)

public sealed record RetryPolicy
{
    public int MaxAttempts { get; init; } = 5;
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(1);
    public double BackoffMultiplier { get; init; } = 2.0;
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(60);
}
```

**Codec/runner pairing.** At startup the host runs `_validateRunnerCodecPairing`: if `runner.PayloadMode == Json` and any push-capable channel does not implement `IChannelPushCodec`, throw `ChannelConfigurationException` so the misconfiguration is caught before traffic.

**In-process runner shutdown drain.** `InProcessDurableTaskRunner` ships a two-phase shutdown driven by `ShutdownGraceSeconds` (default 5s). After lifespan shutdown signals, in-flight `"hosting.push"` tasks are given the grace period to finish; on expiry, remaining tasks are cancelled and their `OperationCanceledException` is swallowed (expected shutdown shape, not logged as a failure).

**Echo idempotency on retry.** The host's `"hosting.push"` handler tracks an `echo_done` cursor on `TaskInvocationContext.State`. A retry after the echo succeeded but before the response push completed will not double-echo. The cursor lives on runner-owned task state, not the message — same principle as "intent only on the message, operational state in the runner".

### Hosted target runner (Foundry-reuse seam)

```csharp
public interface IHostedTargetRunner
{
    ValueTask<HostedRunResult> RunAsync(ChannelRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<HostedStreamItem> StreamAsync(ChannelRequest request, CancellationToken cancellationToken);
}

// Built into core:
internal sealed class AIAgentRunner(AIAgent agent) : IHostedTargetRunner { /* ... */ }
internal sealed class WorkflowRunner(Workflow workflow) : IHostedTargetRunner { /* ... */ }

// Lives in Microsoft.Agents.AI.Foundry.Hosting (additive — no break to existing surface):
public sealed class FoundryHostedAgentRunner(FoundryHostedAgentHandle handle) : IHostedTargetRunner { /* ... */ }
```

### Workflow channels: resume model

Channels never branch on target type. The workflow story is carried by:

- `WorkflowRunner : IHostedTargetRunner` (returns `HostedRunResult<WorkflowRunResponse>`).
- `ChannelRequest.Attributes` reserved keys:
  - `"workflow.resume_token"` (string) — **opaque host-issued correlation id** persisted on `IHostStateStore` as a `ContinuationToken` whose `Status = "awaiting_input"` and whose payload includes (a) the workflow instance reference and (b) the originating `RequestInfoEvent.Request`. Issued by the host whenever the workflow emits a `RequestInfoEvent` and the channel responds with a "needs input" envelope. The caller posts back with the same token to resume.
  - `"workflow.checkpoint_id"` (string) — **direct opt-in for advanced callers** who already know the workflow checkpoint id (e.g. a UI that wants to fork from a specific past state). Passed straight to `Workflow.RunAsync(checkpointId: ...)`. Mutually exclusive with `workflow.resume_token`.
- `WorkflowInvocationsResponseHook` (ships in `.Invocations`) renders `RequestInfoEvent` as `{ "status": "awaiting_input", "request": {...}, "resume_token": "..." }`. App authors handle `RequestInfoEvent` rendering for Telegram / Responses via their own `IChannelResponseHook`.

The host-side wiring: when `WorkflowRunner` produces a `WorkflowRunResponse` that contains a pending `RequestInfoEvent`, the host issues a `ContinuationToken`, stores it on `IHostStateStore`, and surfaces the token via `HostedRunResult.Session.Attributes["workflow.resume_token"]`. The channel's response hook reads it and projects it onto the wire. On the next request carrying `Attributes["workflow.resume_token"]`, the host looks up the `ContinuationToken`, retrieves the workflow instance + correlation id, and calls `Workflow.RunAsync(resumeToken: ...)`.

### Built-in routes

For built-in channels, `Channel.Path` is the configurable mount root. The channel package owns the fixed protocol-relative suffix. Final route = `Path` + suffix.

| Channel | Default `Path` | Default exposed route(s) |
|---|---|---|
| `ResponsesChannel` | `/responses` | `/responses/v1` and nested response/conversation routes |
| `InvocationsChannel` | `/invocations` | `/invocations/invoke` (sync) and `/invocations/{continuationToken}` (poll) |
| `TelegramChannel` | `/telegram` | webhook transport: `/telegram/webhook`; polling transport: no required HTTP route (uses `IHostedService` long-poll loop) |

Overrides only replace the outer mount root:

```csharp
builder.AddAgentFrameworkHost(agent)
    .AddResponsesChannel(o => o.Path = "/public/responses")        // -> /public/responses/v1
    .AddInvocationsChannel(o => o.Path = "/internal/invocations")  // -> /internal/invocations/invoke
    .AddTelegramChannel(o => o.Path = "/bots/telegram");           // -> /bots/telegram/webhook
```

### Multi-user conversations

Telegram groups, Telegram forum topics, and future Teams group chats / team channels share a uniform contract. Two axes that channels MUST keep separate:

- `ChannelIdentity.NativeId` is always the **user** (`from.id` / AAD `oid`). In 1:1 chats it often coincides with the chat id; in groups it does not.
- `ChannelRequest.ConversationId` is the **chat / channel / thread** locator.

Channels expose `ConversationScope` controlling how the host derives the resolved isolation key in multi-user surfaces:

| Scope | Isolation key derivation in multi-user conversations | Pick when |
|---|---|---|
| `PerUser` | The user's isolation key from identity resolution only — group and DM share state. | Personal-assistant agents where memory follows the user. Risky if the agent emits user-specific data in a public group. |
| `PerUserPerConversation` (default for multi-user) | `f"{userIsolationKey}:{conversationId}"` — same user gets a different isolation key per group / channel / topic / DM. | Default and safest. Per-conversation memory isolation. |
| `PerConversation` | `f"_conv:{channel}:{conversationId}"` — every member of the group shares one isolation key and one `AgentSession`. | "Bot lives in this channel" — meeting-notes bot, shared scratchpad, support-triage queue. |

1:1 chats always derive the isolation key from the user identity alone.

`AcceptInGroup` controls inbound filtering on group surfaces:

| Mode | Semantics | Default for |
|---|---|---|
| `MentionOnly` | Accept only `@bot` mentions. | Telegram groups, future Teams group chats / channels |
| `CommandOnly` | Accept only registered `ChannelCommand` invocations. | — |
| `MentionOrCommand` | Either of the above. | — |
| `All` | Accept every inbound message. | 1:1 chats; opt-in for groups when the bot is the only conversational participant |

Messages not satisfying the rule are filtered at the channel layer — no `ChannelRequest` is produced and the agent is never invoked.

**Link ceremonies in groups MUST NOT post the challenge URL or code into a group conversation.** Channels detect group context (via `ConversationContext.IsGroup`) and, when `RequireLink = true` triggers a `LinkChallenge`, redirect the rendered challenge to the user's DM. Verified-claim auto-link is unaffected: a Teams `groupChat` request carrying an AAD-verified `from.aadObjectId` that already matches an existing claim in the link store merges silently with no group-visible artifact.

### Channel session-carriage models

Channels split into two families based on who owns the session identifier across requests:

| Model | Examples | `ChannelSession.Key` source | "New thread" UX |
|---|---|---|---|
| **Caller-supplied session** | Responses, Invocations, A2A, MCP | Wire payload (`previous_response_id`, `conversation_id`, body `session_id`). `null` means ephemeral. | Caller omits the previous id. |
| **Host-tracked session** | Telegram, Activity Protocol, future WhatsApp / Discord | Channel leaves `ChannelSession.Key = null`; host alias decides which `AgentSession` to resolve. | Channel exposes a `/new`-style `ChannelCommand` that calls `host.ResetSessionAsync(isolationKey)`. |

`host.ResetSessionAsync(isolationKey)` rotates the active session-id alias rather than deleting on-disk history: prior history remains addressable by its original session id; subsequent runs for that `IsolationKey` resolve to a brand-new `AgentSession`. Caller-supplied channels do not call `ResetSessionAsync`.

A single `AgentFrameworkHost` mounts channels from both families. A user can chat on Telegram (host-tracked) and have it linked via `IIdentityLinker` to a Responses-channel session keyed by `previous_response_id`; the linker's identity merge collapses both sides onto the same `IsolationKey`.

### Isolation keys (Foundry runtime partition hints)

```csharp
public sealed record IsolationKeys(string? UserKey, string? ChatKey)
{
    public static AsyncLocal<IsolationKeys?> CurrentSlot { get; } = new();
    public static IsolationKeys? Current
    {
        get => CurrentSlot.Value;
        set => CurrentSlot.Value = value;
    }

    public bool IsEmpty => UserKey is null && ChatKey is null;

    public const string UserHeader = "x-agent-user-isolation-key";
    public const string ChatHeader = "x-agent-chat-isolation-key";
}

public interface IIsolationKeysAccessor
{
    IsolationKeys? Current { get; }
}
```

`IsolationKeys` is **distinct from** the app-level isolation key produced by `IIdentityLinker`. It carries the Foundry runtime's per-request partition hints lifted off `x-agent-user-isolation-key` / `x-agent-chat-isolation-key` headers by middleware the host registers automatically. Providers (a future Foundry-partitioned history/state store) read `IsolationKeys.Current` to scope backend calls. Channels themselves are oblivious. v1 ships the plumbing; the first consumer lands as a fast-follow `FoundryHistoryProvider`.

### Intended targets and durable delivery

When `ResponseTarget != Originating`, the host fans the response out using a synchronous-on-originating, scheduled-elsewhere pattern:

1. The host runs the target once. The result is a single `HostedRunResult`.
2. The channel calls `IChannelContext.ScheduleResponseAsync(result, originatingRequest, ...)`. Internally the host resolves the destination set (consulting `IHostStateStore.GetLastSeenAsync` for `Active`, `GetIdentitiesAsync` for `AllLinked`, and `ILinkPolicy.EvaluateAsync` to filter every entry).
3. If `Originating` is one of the destinations (i.e. when the target is `AllLinked` / `Channels([..., originating])` / etc.), the originating channel renders that destination synchronously on the inbound HTTP response (or polling reply) so the caller sees the answer without waiting for the durable runner.
4. For every non-originating destination, the host schedules one push task per destination on `IDurableTaskRunner` under the reserved handler name `"hosting.push"`. The runner invokes `IChannelPush.PushAsync(channelPushContext, payload)` with the appropriate `ChannelPushContext`.
5. Per-destination `IChannelResponseHook.OnResponseAsync` runs **inside** the push task immediately before `PushAsync`, so per-channel rebinds (e.g. JSON dump rendering for one channel, plain-text rendering for another) do not block the originating reply.

**Intended-targets bookkeeping (persisted on the assistant message).** The host writes a single envelope to the assistant message's `additional_properties["hosting"]` describing the routing decision at the moment of dispatch:

```json
{
  "hosting": {
    "originating_channel": "responses",
    "response_target": { "kind": "all_linked", "echo_input": true },
    "intended_targets": [
      { "channel": "responses", "native_id": "user_42", "echo": false },
      { "channel": "telegram",  "native_id": "12345678", "echo": true }
    ],
    "skipped_targets": [
      { "channel": "discord", "native_id": "8675309", "reason": "link_policy" }
    ]
  }
}
```

`intended_targets[]` is immutable once written and represents intent. `skipped_targets[]` carries pre-dispatch filtering (link policy, missing `IChannelPush` capability, all-channel resolution returning empty for a given identity). Per-destination delivery failures live on `IDurableTaskRunner` task state, not the message — same partition rule.



## E2E code samples

### Sample 1: Responses + Telegram sharing one agent and one session per user

```csharp
using Microsoft.Agents.AI.Hosting.Channels;
using Microsoft.Agents.AI.Hosting.Channels.Responses;
using Microsoft.Agents.AI.Hosting.Channels.Telegram;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

var agent = new AzureOpenAIChatClient(/* ... */)
    .CreateAIAgent(name: "Concierge", instructions: "You are a helpful concierge.");

builder.AddAgentFrameworkHost(agent, o =>
    {
        o.DefaultAllowlist = new AnyOfIdentityAllowlist(
            AuthorizationProfile.LinkedClaimAllowlist("email", "*@contoso.com"),
            AuthorizationProfile.NativeAllowlist("telegram", "12345678"));
        o.StatePaths = new HostStatePathOptions { Root = "./.afhost" };
    })
    .UseIdentityLinker<OneTimeCodeIdentityLinker>()
    .UseHostStateStore<FileHostStateStore>()
    .AddResponsesChannel()
    .AddTelegramChannel(o =>
    {
        o.BotToken = builder.Configuration["Telegram:BotToken"]!;
        o.ConversationScope = ConversationScope.PerUserPerConversation;
        o.AcceptInGroup = AcceptInGroup.MentionOnly;
    });

var app = builder.Build();
app.MapAgentFrameworkHost();
app.Run();
```

End-state behavior:

1. A Responses API client posts with `previous_response_id`. The host resolves a `ChannelSession` keyed by the user's resolved `IsolationKey` and re-uses the existing `AgentSession`.
2. The same user messages Telegram (the user's Telegram id is in the `NativeIdAllowlist`, so they are admitted pre-link with an auto-issued isolation key). To collapse to the existing Responses-side isolation key they type `/link <code>` once. `OneTimeCodeIdentityLinker.CompleteAsync` calls `IHostStateStore.SaveLinkAsync(telegramIdentity, responsesIsolationKey, ...)` which atomically merges the Telegram native id onto the same isolation key.
3. The next Telegram message hits the same `AgentSession` the Responses client was using.
4. Server-side push back to Telegram works through `TelegramChannel : IChannelPush`, registered as a durable-task handler at host startup. The Telegram `/new` command calls `host.ResetSessionAsync(isolationKey)` to start a fresh conversation without losing history.

### Sample 2: Workflow target on InvocationsChannel with `RequestInfoEvent` rendering

```csharp
using Microsoft.Agents.AI.Hosting.Channels;
using Microsoft.Agents.AI.Hosting.Channels.Invocations;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

var workflow = new WorkflowBuilder(checkpointStorage: new FileCheckpointStorage("./.checkpoints"))
    .AddExecutor(/* application-defined intake executor */)
    .Build();

builder.AddAgentFrameworkHost(workflow)
    .AddInvocationsChannel();  // WorkflowInvocationsResponseHook registered automatically

var app = builder.Build();
app.MapAgentFrameworkHost();
app.Run();
```

Inbound:

```
POST /invocations/invoke
{ "input": { "customerId": "...", "sku": "...", "quantity": 12 } }
```

If the workflow pauses on a `RequestInfoEvent`, `WorkflowInvocationsResponseHook` renders:

```json
{
  "status": "awaiting_input",
  "request": { /* the RequestInfoEvent.Request payload */ },
  "resume_token": "<continuation-token>"
}
```

The host stored the workflow instance reference + correlation id under the continuation token. To resume, the caller posts with `Attributes["workflow.resume_token"]` set:

```
POST /invocations/invoke
{
  "input": { "approved": true, "approver": "alice" },
  "attributes": { "workflow.resume_token": "<continuation-token>" }
}
```

The host promotes the attribute onto `ChannelRequest.Attributes`, `WorkflowRunner` reads `"workflow.resume_token"`, looks the entry up via `IHostStateStore.GetContinuationAsync(...)`, retrieves the persisted correlation id and workflow reference, then calls `Workflow.RunAsync(resumeToken: ...)`. Workflow checkpoint storage remains on `WorkflowBuilder` (`FileCheckpointStorage` here) and is never touched by the host.

### Sample 3: Foundry hosted agent as target — same channels, same builder

```csharp
using Microsoft.Agents.AI.Hosting.Channels.Responses;
using Microsoft.Extensions.Hosting;
using Microsoft.Agents.AI.Foundry.Hosting;   // brings the AddFoundryHostedAgent overload

var foundryHandle = await foundryClient.GetHostedAgentAsync("my-agent");

builder.AddFoundryHostedAgent(foundryHandle)   // resolves FoundryHostedAgentRunner
    .AddResponsesChannel();
```

The `Microsoft.Agents.AI.Foundry.Hosting` package supplies an `AddFoundryHostedAgent(IHostApplicationBuilder, FoundryHostedAgentHandle, ...)` extension that internally calls `AddAgentFrameworkHost<FoundryHostedAgentHandle>(_ => handle)` and registers `FoundryHostedAgentRunner` as the `IHostedTargetRunner`. The `ResponsesChannel` code is identical to Sample 1. Only the registered `IHostedTargetRunner` differs.

### Sample 4: Authoring a new channel package

```csharp
public sealed class MyWebhookChannel : Channel, IChannelPush
{
    public override string Name => "mywebhook";
    public override string Path => "/mywebhook";

    public override ChannelContribution Contribute(IChannelContext context) => new()
    {
        Routes =
        [
            // The host wraps this action in endpoints.MapGroup(Path), so "/inbound" mounts at "/mywebhook/inbound".
            endpoints => endpoints.MapPost("/inbound", async (HttpContext http) =>
            {
                var payload = await JsonSerializer.DeserializeAsync<MyInboundPayload>(http.Request.Body)
                    ?? throw new InvalidOperationException("empty body");

                var identity = new ChannelIdentity(Channel: Name, NativeId: payload.AccountId);

                // Funnel through the host's authorization pipeline before invoking the target.
                var auth = await context.AuthorizeAsync(identity, new AuthorizationRequest { }, http.RequestAborted);
                if (auth is AuthorizationOutcome.Denied denied)
                    return Results.Forbid(authenticationSchemes: [denied.ReasonCode]);
                if (auth is AuthorizationOutcome.LinkRequired link)
                    return Results.Json(new { status = "link_required", challenge = link.Challenge });

                var allowed = (AuthorizationOutcome.Allowed)auth;

                var request = new ChannelRequest
                {
                    Channel = Name,
                    Operation = "message.create",
                    Input = payload.Text,
                    Identity = identity,
                    Session = new ChannelSession
                    {
                        Key = payload.ThreadId,
                        ConversationId = payload.ThreadId,
                        IsolationKey = allowed.IsolationKey,
                    },
                };

                var result = await context.RunAsync(request, http.RequestAborted);
                return Results.Json(MyOutboundPayload.From(result));
            }),
        ],
    };

    // The host invokes this from "hosting.push" durable tasks for every non-originating destination.
    public ValueTask PushAsync(ChannelPushContext context, HostedRunResult payload, CancellationToken cancellationToken)
    {
        // Send to context.Destination.NativeId using whatever HTTP/SDK call this protocol needs.
        return ValueTask.CompletedTask;
    }
}
```

For richer scenarios the channel additionally implements `IChannelPushCodec` (required when paired with a JSON-payload durable runner), `IChannelRunHook`, `IChannelResponseHook`, or `IChannelStreamTransformHook`. Each capability is independent.



## Migration story

**v1: nothing changes for existing consumers.** Existing `MapOpenAIResponses`, `MapA2A`, `MapAGUI`, Foundry hosting, and Azure Functions handlers continue to ship and behave exactly as today. No `[Obsolete]` warnings, no shim code paths, no change in behavior or surface.

**Fast follow (Tier 2):** internals of existing `Map*` extensions get rewritten to delegate to the new builder + a private channel. From the consumer's view this is invisible. An `[Obsolete]` recommendation pointing at the new builder ships at the same time so new code uses the new surface. Existing consumers get a deprecation warning but no break, and have at least one full release of overlap before any removal is considered.

## Test strategy

| Layer | Test type | What it proves |
|---|---|---|
| Channel contract | Unit | `Channel.Contribute` returns a contribution; capability interfaces are independently implementable. |
| `AgentFrameworkHost` composition | Unit | `AddAgentFrameworkHost` + N `AddXxxChannel` produces a host whose `Channels` list matches; channel `ConfigureServices` runs pre-`Build`; `Contribute` runs post-`Build`. |
| `ResponsesChannel` wire compat | Integration (`TestServer`) | Post a Responses-shape request; assert the response round-trips the full `ChatMessage` content list (no lossy collapse to a single text field). |
| `InvocationsChannel` workflow path | Integration | Target a `Workflow`; `RequestInfoEvent` rendered by `WorkflowInvocationsResponseHook` produces the documented envelope; a subsequent request with `workflow.resume_token` resumes correctly. |
| `TelegramChannel` | Integration (mocked `Telegram.Bot`) | Inbound update produces a `ChannelRequest` with the correct identity; `IChannelPush.PushAsync` calls `sendMessage`. |
| Identity stack | Unit | `AnyOfIdentityAllowlist` short-circuits on first `Allow`; `Abstain` defers; `Deny` wins over `Abstain`. |
| `OneTimeCodeIdentityLinker` | Integration | End-to-end: begin produces a code, complete on the other channel collapses isolation keys, subsequent requests on either channel resolve to the same session. |
| `IHostStateStore` (file impl) | Unit | Per-component path overrides land on the right folders; missing paths fall back to in-memory; concurrent writes do not corrupt. |
| `InProcessDurableTaskRunner` | Unit + property | Schedule / Get / Cancel / Resume round-trip; bounded `Channel<T>` does not drop; disk persistence replays after restart when `RunnerPath` is set. |
| `IsolationKeys` middleware | Unit (`TestServer`) | Headers lift into `IsolationKeys.Current` for the request scope; reset after; absent headers leave `Current` null. |
| `FoundryHostedAgentRunner` | Integration | Same channels work transparently against a Foundry hosted-agent handle as against a local `AIAgent`. |
| End-to-end smoke | Integration | Sample 1 above runs in-process, posts via Responses, asserts a Telegram push fires, asserts session continuity across channels. |

## Phasing

1. **Core abstractions** — `Microsoft.Agents.AI.Hosting.Channels` package: `Channel` / `ChannelContribution` / `ChannelRequest` / `ChannelSession` / `ChannelIdentity` / `ResponseTarget` / `HostedRunResult` / `HostedStreamItem` / `IChannelContext` / capability interfaces / `IHostedTargetRunner` + built-in `AIAgentRunner` + `WorkflowRunner` / `IsolationKeys` plumbing. Identity registry primitives on `IHostStateStore` (`GetIsolationKeyAsync` / `SaveLinkAsync` / `LookupByVerifiedClaimAsync` / `RotateSessionAliasAsync`) and continuation tokens (`Save/Get/DeleteContinuationAsync`) ship in this phase — the host cannot resolve a session without them. `InMemoryHostStateStore` + `FileHostStateStore`. `InProcessDurableTaskRunner` + `RetryPolicy` + `TaskHandle` + `DurableTaskPayloadMode` + codec/runner pairing validation. Host startup validator (fail-fast rules). Unit tests per type.
2. **Identity allowlists + linker** — `IIdentityAllowlist` tri-state + `AllowAllIdentityAllowlist` / `NativeIdAllowlist` / `LinkedClaimAllowlist` / `AnyOfIdentityAllowlist` / `AllOfIdentityAllowlist` / `AuthorizationProfile` factory. `IIdentityLinker` + `OneTimeCodeIdentityLinker` (zero deps). `ILinkPolicy` + 4 built-ins. `host.AuthorizeAsync` pipeline + per-channel inheritance semantics. End-to-end integration test of cross-channel session collapse using `OneTimeCodeIdentityLinker` over an in-memory pair of dummy channels.
3. **ResponsesChannel + InvocationsChannel packages** — both prove a single host with two channels resolves to the same session under identity-linking. `WorkflowInvocationsResponseHook` ships with the Invocations package; `Attributes["workflow.resume_token"]` round-trip integration test.
4. **TelegramChannel package** — proves polling `IHostedService` lifecycle, webhook transport, `IChannelPush` + `IChannelPushCodec`, command registration via `setMyCommands`, group-vs-DM filtering (`AcceptInGroup`), per-conversation isolation (`ConversationScope`), link-challenge group-safety redirect to DM.
5. **Foundry runner adapter** — `FoundryHostedAgentRunner` lands in existing `Microsoft.Agents.AI.Foundry.Hosting` as an additive type, along with `AddFoundryHostedAgent` overload on `IHostApplicationBuilder`. Integration test against a mocked Foundry hosted-agent handle.
6. **Samples + docs** — port the cross-channel-continuity Python sample to .NET. README per package. Worked Telegram-and-Responses sample exercising every locked decision end-to-end.

## Fast-follow work (out of v1)

- `Microsoft.Agents.AI.Hosting.Channels.DurableTask` adapter package wrapping `Microsoft.Agents.AI.DurableTask` (DTF). Validates the JSON payload codec path against a real out-of-process runner.
- `Microsoft.Agents.AI.Hosting.Channels.Discord` (mirrors Python PR #6081).
- `Microsoft.Agents.AI.Hosting.Channels.Activity` (Teams / DirectLine / WebChat via the Activity Protocol). Validates `EmitsVerifiedClaims = true` on the inbound bearer path.
- `Microsoft.Agents.AI.Hosting.Channels.EntraId` shipping `EntraIdentityLinker` with Entra / MSAL dependencies.
- Tier 2 migration: rewrite existing `MapOpenAIResponses` / `MapA2A` / `MapAGUI` internals to delegate to the new builder, ship `[Obsolete]` recommendations pointing at the new surface.
- Foundry-partitioned `IHostStateStore` provider that reads `IsolationKeys.Current` — the consumer that justifies the plumbing landing in v1.
- `IChannelCommandRegistrar` capability interface (registers slash commands with the native protocol — Telegram `setMyCommands`, Discord application commands).
- `AspNetCoreIdentityAllowlistAdapter` bridging `IIdentityAllowlist` to `Microsoft.AspNetCore.Authorization` policies for apps already standardized on that pipeline.

## Open implementation questions

These are *implementation-detail* questions to resolve in code review, not blocking the design:

- Whether `ChannelCommand` registration ships in v1 as a passive metadata record on `ChannelContribution` (channels read their own commands and call the native registration API themselves) or whether `IChannelCommandRegistrar` ships in v1. Default plan: passive record in v1, registrar capability in fast follow.
- Where `AspNetCoreIdentityAllowlistAdapter` lives. Default plan: separate `Microsoft.Agents.AI.Hosting.Channels.AspNetCore` package post-v1, so the core hosting package stays ASP.NET-Core-free for channel authors.
- Whether `FileHostStateStore`'s on-disk schema is documented for external tools to read, or treated as private. Default plan: private in v1, document if a real external use case appears.

## References

- Python spec: [`002-python-hosting-channels.md`](./002-python-hosting-channels.md) — canonical source of truth for cross-language semantics.
- Python source branch: [`feature/python-hosting`](https://github.com/microsoft/agent-framework/tree/feature/python-hosting).
- Python Discord channel PR (fast-follow reference): [microsoft/agent-framework#6081](https://github.com/microsoft/agent-framework/pull/6081).
- Existing .NET hosting packages this work coexists with: `Microsoft.Agents.AI.Hosting.OpenAI`, `Microsoft.Agents.AI.Hosting.A2A`, `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`, `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`, `Microsoft.Agents.AI.Hosting.AzureFunctions`, `Microsoft.Agents.AI.Foundry.Hosting`, `Microsoft.Agents.AI.DurableTask`.
