---
status: proposed
contact: RogerBarreto
date: 2026-05-28
deciders: RogerBarreto
informed: eavanvalkenburg, agent-framework dotnet contributors
---

# .NET minimal hosting core and pluggable channels

> **Posture: translate, minimal core first.** Scope mirrors [ADR-0027](../decisions/0027-hosting-channels.md)
> ("minimal hosting core and pluggable channels") exactly. The richer cross-channel behaviors (identity
> linking, authorization/allowlists, response targeting beyond the originating channel, push/codecs,
> background + continuation delivery, durable runners, retry/replay, link policy, confidentiality tiers,
> multicast) are **out of scope for v1** and tracked by [ADR-0028](../decisions/0028-hosting-linking-multicast-enhancements.md).
> The Python implementation (`agent-framework-hosting` + `agent-framework-hosting-responses`) is the
> canonical reference; this spec records the idiomatic .NET shape (`IHostApplicationBuilder` +
> `IEndpointRouteBuilder` composition on ASP.NET Core / Generic Host). Where this spec is silent, ADR-0027
> governs.

## What are the business goals for this feature?

Give .NET app authors one low-level hosting surface that exposes a single **hostable target** — an `AIAgent`
or a `Workflow` — on one or more **channels** without writing per-protocol routing or server glue, with
**explicit, debuggable session continuity** via a channel-supplied `ChannelSession(IsolationKey)`.

The first slice ships the channel-neutral host plus one channel package (Responses) so the host/channel
boundary can be implemented, tested, and explained without designing identity linking or durable delivery
at the same time.

**Success criteria (mirror ADR-0027 validation gates):**

- A sample exposes one target over the Responses channel with one `AgentFrameworkHost` and a single
  `app.MapAgentFrameworkHost()` call. No hand-written route composition.
- Channel tests prove routes, commands, startup, and shutdown callbacks are contributed by channels and
  aggregated by the host.
- Session tests prove identical `ChannelSession.IsolationKey` values resolve to the **same** cached
  `AgentSession`, and `ResetSession` rotates that mapping.
- Each channel renders only its own originating response; there is no host-level push, multicast, or
  active-channel delivery.
- A workflow sample uses an explicit checkpoint location.

## What is the problem being solved?

Every protocol surface today is its own package with its own `Map*` extension. A developer exposing one
agent over two protocols stands up two hosts and stitches lifecycle, routing, and session handling by hand.
The gap is between **owning a hostable target** and **operationalizing it on a channel**: Agent Framework
already provides agents, workflows, sessions, run inputs, streaming, and the `AIAgent` / `Workflow`
execution seams. What is missing is a small channel-neutral host that owns route/lifecycle aggregation,
target invocation, `IsolationKey -> AgentSession` resolution + caching, per-channel hooks, and workflow
checkpoint wiring — and leaves protocol shape inside channel packages.

## Decisions

1. **Posture: minimal core, translate.** Scope = ADR-0027. ADR-0028 capabilities are deferred and must not
   appear in the v1 public surface.
2. **Composition: builder-centric.** `builder.AddAgentFrameworkHost(target).AddXxxChannel(...)` then
   `app.MapAgentFrameworkHost()`.
3. **Channel contract: `abstract class Channel` + hook interfaces.** `Channel` exposes `Name`, `Path`,
   `ConfigureServices`, `Contribute`. `ChannelContribution` (record, init-only) carries routes, endpoint
   filters (middleware), commands, and startup/shutdown callbacks. Optional per-channel hooks
   (`IChannelRunHook`, `IChannelResponseHook`, `IChannelStreamTransformHook`) are small separate interfaces.
4. **Channel lifecycle: two-phase.** `ConfigureServices(IServiceCollection)` at `AddXxxChannel` time;
   `Contribute(IChannelContext)` at `MapAgentFrameworkHost` time. Matches the ASP.NET Core
   `ConfigureServices` + `Configure` split.
5. **Target runner: swappable `IHostedTargetRunner`.** `AIAgentRunner` for `AIAgent`, `WorkflowRunner` for
   `Workflow`. Channels never branch on target type. (A Foundry hosted-agent runner is a later, additive
   package — not part of this slice.)
6. **Session continuity: explicit `ChannelSession(IsolationKey)`.** The host treats `IsolationKey` as an
   opaque **session partition key**, not proof of identity. Channels / host middleware must authenticate and
   authorize any externally supplied value before passing it to the host. Two channels share history only
   when they produce the same `IsolationKey`.
7. **`ChannelIdentity` is request metadata only.** In v1 it is **not** a linking, authorization, or delivery
   key. (Those uses are ADR-0028.)
8. **Host state store: new `IHostStateStore`, limited.** Owns only reset-session aliases and workflow
   checkpoint path derivation. It does **not** store linked identities, active-channel state, response
   routing, continuation records, durable runner queues, or delivery attempts (all ADR-0028). Ships
   `InMemoryHostStateStore` and `FileHostStateStore`.
9. **Hosting target: Generic Host + ASP.NET Core.** `AddAgentFrameworkHost(this IHostApplicationBuilder, target)`
   accepts both `WebApplicationBuilder` and `HostApplicationBuilder`. HTTP routes via
   `app.MapAgentFrameworkHost(this IEndpointRouteBuilder)`. Non-HTTP channels auto-start via `IHostedService`.
10. **Naming: literal port.** Host type `AgentFrameworkHost`; extensions `AddAgentFrameworkHost(...)` /
    `MapAgentFrameworkHost(...)`; channel-add extensions `AddResponsesChannel(...)`.
11. **Isolation context propagation: static `IsolationKeys.Current` + DI `IIsolationKeysAccessor`,** both
    backed by `AsyncLocal<IsolationKeys?>`, lifted from `x-agent-user-isolation-key` /
    `x-agent-chat-isolation-key` by host middleware **only when the Foundry hosting environment flag is
    present**. Distinct from the app-level `IsolationKey`. v1 ships the plumbing; reusing the header names
    does not make this the supported Foundry Hosted Agents surface.
12. **Workflow channel surface: workflow-agnostic channels.** Carried by `WorkflowRunner : IHostedTargetRunner`,
    generic `HostedRunResult<TResult>`, free-form `ChannelRequest.Attributes` (reserved key
    `workflow.checkpoint_id` for caller-supplied checkpoint resume), and workflow checkpoint storage on
    `WorkflowBuilder`. The host does not own a continuation store in v1.
13. **v1 scope: net-new only.** Existing `Hosting.OpenAI` / `Hosting.A2A*` / `Hosting.AGUI.AspNetCore` /
    `Hosting.AzureFunctions` / `Foundry.Hosting` `Map*` extensions stay untouched.

## Package layout

### v1 NuGet packages (new)

```
Microsoft.Agents.AI.Hosting.Channels                 (core)
├── AgentFrameworkHost
├── AgentFrameworkHostOptions                         (StatePaths)
├── IAgentFrameworkHostBuilder
├── HostApplicationBuilderHostingChannelsExtensions   (AddAgentFrameworkHost on IHostApplicationBuilder)
├── EndpointRouteBuilderHostingChannelsExtensions     (MapAgentFrameworkHost on IEndpointRouteBuilder)
├── Channel                                           (abstract class)
├── ChannelContribution                               (record, init-only)
├── ChannelCommand
├── ChannelCommandContext
├── ChannelRequest                                    (record)
├── ChannelSession                                    (record; Key / ConversationId / IsolationKey nullable)
├── SessionMode                                       (enum: Auto / Required / Disabled)
├── ChannelIdentity                                   (request metadata only)
├── HostedRunResult                                   (non-generic base)
├── HostedRunResult<TResult>                          (generic envelope)
├── HostedStreamItem                                  (Update / Event / Completed)
├── IChannelContext
├── IChannelRunHook        + ChannelRunHookContext
├── IChannelResponseHook   + ChannelResponseContext
├── IChannelStreamTransformHook
├── IHostedTargetRunner
│   ├── AIAgentRunner
│   └── WorkflowRunner
├── WorkflowRunResult                                 (Completed / AwaitingInput / Failed)
├── IHostStateStore                                   (reset-session aliases + checkpoint path only)
├── InMemoryHostStateStore
├── FileHostStateStore
├── HostStatePathOptions                              (Root / RunnerPath-free; Aliases / Checkpoints)
├── IsolationKeys
├── IIsolationKeysAccessor
└── IsolationKeysMiddleware                           (Foundry-flag gated header lift)

Microsoft.Agents.AI.Hosting.Channels.Responses
├── ResponsesChannel
├── ResponsesChannelOptions                           (Path / RunHook / ResponseHook)
└── AgentFrameworkHostBuilderResponsesExtensions      (AddResponsesChannel)
```

### v1 NuGet packages (untouched)

`Microsoft.Agents.AI.Hosting.OpenAI`, `.Hosting.A2A`, `.Hosting.A2A.AspNetCore`, `.Hosting.AGUI.AspNetCore`,
`.Hosting.AzureFunctions`, `.Foundry.Hosting`. The Responses channel reuses Responses models / parsing from
`Microsoft.Agents.AI.Hosting.OpenAI` where practical rather than reimplementing the wire format.

## API changes

> Draft signatures; nullability and `Experimental` attributes sharpen during implementation. Every public
> type ships the standard copyright header and is `[Experimental(...)]` for the v1 release.
>
> **Namespace convention.** Public types live in `Microsoft.Agents.AI.Hosting.Channels` (and `*.Responses`).
> `IHostApplicationBuilder` extensions live in `namespace Microsoft.Extensions.Hosting`; `IEndpointRouteBuilder`
> extensions in `namespace Microsoft.AspNetCore.Builder`; channel-add extensions in
> `Microsoft.Agents.AI.Hosting.Channels`.

### Host + builder

```csharp
namespace Microsoft.Agents.AI.Hosting.Channels;

public sealed class AgentFrameworkHost
{
    public IServiceProvider Services { get; }
    public IReadOnlyList<Channel> Channels { get; }
    public IHostedTargetRunner TargetRunner { get; }
    public IHostStateStore StateStore { get; }
    public AgentFrameworkHostOptions Options { get; }

    public ValueTask<HostedRunResult> RunAsync(ChannelRequest request, CancellationToken cancellationToken = default);
    public IAsyncEnumerable<HostedStreamItem> StreamAsync(ChannelRequest request, CancellationToken cancellationToken = default);

    // Rotates the active session-id alias for an isolation key (host-tracked channels' /new-style commands).
    public ValueTask ResetSessionAsync(string isolationKey, CancellationToken cancellationToken = default);
}

public sealed record AgentFrameworkHostOptions
{
    public HostStatePathOptions? StatePaths { get; init; }
}
```

```csharp
namespace Microsoft.Extensions.Hosting;

public static class HostApplicationBuilderHostingChannelsExtensions
{
    public static IAgentFrameworkHostBuilder AddAgentFrameworkHost(
        this IHostApplicationBuilder builder, AIAgent target, Action<AgentFrameworkHostOptions>? configure = null);

    public static IAgentFrameworkHostBuilder AddAgentFrameworkHost(
        this IHostApplicationBuilder builder, Workflow target, Action<AgentFrameworkHostOptions>? configure = null);

    public static IAgentFrameworkHostBuilder AddAgentFrameworkHost<TTarget>(
        this IHostApplicationBuilder builder, Func<IServiceProvider, TTarget> targetFactory,
        Action<AgentFrameworkHostOptions>? configure = null) where TTarget : class;
}
```

```csharp
namespace Microsoft.AspNetCore.Builder;

public static class EndpointRouteBuilderHostingChannelsExtensions
{
    public static IEndpointConventionBuilder MapAgentFrameworkHost(this IEndpointRouteBuilder endpoints);
}
```

```csharp
namespace Microsoft.Agents.AI.Hosting.Channels;

public interface IAgentFrameworkHostBuilder
{
    IServiceCollection Services { get; }
    AgentFrameworkHostOptions Options { get; }

    IAgentFrameworkHostBuilder AddChannel(Channel channel);
    IAgentFrameworkHostBuilder AddChannel<TChannel>(Func<IServiceProvider, TChannel> factory) where TChannel : Channel;

    IAgentFrameworkHostBuilder UseHostStateStore<TStore>() where TStore : class, IHostStateStore;
}
```

### Channel contract

```csharp
public abstract class Channel
{
    public abstract string Name { get; }
    public virtual string Path => string.Empty;       // host wraps Routes in endpoints.MapGroup(Path)
    public virtual void ConfigureServices(IServiceCollection services) { }
    public abstract ChannelContribution Contribute(IChannelContext context);
}

public sealed record ChannelContribution
{
    public IReadOnlyList<Action<IEndpointRouteBuilder>> Routes { get; init; } = [];
    public IReadOnlyList<IEndpointFilter> EndpointFilters { get; init; } = [];   // host-level middleware
    public IReadOnlyList<ChannelCommand> Commands { get; init; } = [];
    public Func<CancellationToken, ValueTask>? OnStartup { get; init; }
    public Func<CancellationToken, ValueTask>? OnShutdown { get; init; }
}

public interface IChannelContext
{
    IServiceProvider Services { get; }
    AgentFrameworkHost Host { get; }
    IHostStateStore StateStore { get; }

    ValueTask<HostedRunResult> RunAsync(ChannelRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<HostedStreamItem> StreamAsync(ChannelRequest request, CancellationToken cancellationToken = default);
}
```

> Hooks are discovered by the host: a channel implements `IChannelRunHook` / `IChannelResponseHook` /
> `IChannelStreamTransformHook` directly, or routes app-supplied hooks through its options record
> (`ResponsesChannelOptions.RunHook`, etc.). `ChannelRunHook` runs after channel parsing and before target
> invocation; `ChannelResponseHook` runs after invocation and before the originating channel serializes;
> `ChannelStreamTransformHook` is applied by the host while the channel consumes streamed updates.

### Channel-neutral request envelope

```csharp
public sealed record ChannelRequest
{
    public required string Channel { get; init; }
    public required string Operation { get; init; }          // "message.create", "command.invoke", ...
    public required object Input { get; init; }              // string / ChatMessage / IEnumerable<ChatMessage> / workflow input
    public ChannelSession? Session { get; init; }
    public ChannelIdentity? Identity { get; init; }          // request metadata only
    public string? ConversationId { get; init; }
    public ChatOptions? Options { get; init; }
    public SessionMode SessionMode { get; init; } = SessionMode.Auto;
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = ImmutableDictionary<string, object?>.Empty;
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = ImmutableDictionary<string, object?>.Empty;
    public bool Stream { get; init; }
}

public sealed record ChannelSession
{
    public string? Key { get; init; }                        // caller-supplied (previous_response_id, ...)
    public string? ConversationId { get; init; }
    public string? IsolationKey { get; init; }               // opaque session partition key
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = ImmutableDictionary<string, object?>.Empty;
}

public sealed record ChannelIdentity(string Channel, string NativeId)
{
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = ImmutableDictionary<string, string>.Empty;
}

public enum SessionMode { Auto, Required, Disabled }
```

### Results + streaming

```csharp
public abstract record HostedRunResult
{
    public ChannelSession? Session { get; init; }
    public abstract object? ResultObject { get; }
}

public sealed record HostedRunResult<TResult> : HostedRunResult
{
    public required TResult Result { get; init; }
    public override object? ResultObject => this.Result;
    public HostedRunResult<TNew> Replace<TNew>(TNew newResult) => new() { Result = newResult, Session = this.Session };
}

public abstract record HostedStreamItem;
public sealed record HostedStreamUpdate(AgentResponseUpdate Update) : HostedStreamItem;   // normalized agent stream
public sealed record HostedStreamEvent(object Event) : HostedStreamItem;                  // protocol/workflow events
public sealed record HostedStreamCompleted(HostedRunResult Result) : HostedStreamItem;    // terminal
```

### Host state store (limited)

```csharp
public interface IHostStateStore
{
    // Session-alias rotation backing ResetSessionAsync (host-tracked channels' /new).
    ValueTask RotateSessionAliasAsync(string isolationKey, CancellationToken cancellationToken);
    ValueTask<string?> GetActiveSessionAliasAsync(string isolationKey, CancellationToken cancellationToken);

    // Workflow checkpoint path derivation for an isolation key.
    ValueTask<string> GetCheckpointLocationAsync(string isolationKey, CancellationToken cancellationToken);
}

public sealed record HostStatePathOptions
{
    public string? Root { get; init; }              // shorthand: derives subpaths if unset
    public string? AliasesPath { get; init; }       // reset-session aliases
    public string? CheckpointsPath { get; init; }   // workflow checkpoint derivation root
}
```

> Workflow checkpoint *storage* stays on `WorkflowBuilder.CheckpointStorage`. `IHostStateStore` only derives
> the per-isolation-key location. There is no continuation store, link grant, last-seen ledger, or identity
> registry in v1.

### Hosted target runner

```csharp
public interface IHostedTargetRunner
{
    ValueTask<HostedRunResult> RunAsync(ChannelRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<HostedStreamItem> StreamAsync(ChannelRequest request, CancellationToken cancellationToken);
}

internal sealed class AIAgentRunner(AIAgent agent) : IHostedTargetRunner { /* ... */ }
internal sealed class WorkflowRunner(Workflow workflow) : IHostedTargetRunner { /* ... */ }
```

`WorkflowRunner` drives `InProcessExecution.RunStreamingAsync`, accumulates `WorkflowOutputEvent`, and pauses
on `RequestInfoEvent` into `WorkflowRunResult { Status = AwaitingInput }`. Resume is **caller-driven** via a
checkpoint reference supplied on `ChannelRequest.Attributes["workflow.checkpoint_id"]`; there is no
host-owned continuation token in v1.

### Isolation keys

```csharp
public sealed record IsolationKeys(string? UserKey, string? ChatKey)
{
    public static AsyncLocal<IsolationKeys?> CurrentSlot { get; } = new();
    public static IsolationKeys? Current { get => CurrentSlot.Value; set => CurrentSlot.Value = value; }
    public bool IsEmpty => this.UserKey is null && this.ChatKey is null;
    public const string UserHeader = "x-agent-user-isolation-key";
    public const string ChatHeader = "x-agent-chat-isolation-key";
}

public interface IIsolationKeysAccessor { IsolationKeys? Current { get; } }
```

`IsolationKeysMiddleware` lifts the headers into `IsolationKeys.Current` **only when the Foundry hosting
environment flag is present**; absent the flag, raw isolation headers are ignored.

## Responses channel

`Microsoft.Agents.AI.Hosting.Channels.Responses` maps OpenAI Responses-shaped requests and streams onto the
host and renders Responses-compatible output, reusing the Responses models / converters / streaming
generators from `Microsoft.Agents.AI.Hosting.OpenAI` where practical.

```csharp
public sealed class ResponsesChannel : Channel
{
    public override string Name => "responses";
    public override string Path => this._options.Path;       // default "/responses"
    public override ChannelContribution Contribute(IChannelContext context);  // POST {Path} + nested response routes
}

public sealed class ResponsesChannelOptions
{
    public string Path { get; set; } = "/responses";
    public IChannelRunHook? RunHook { get; set; }
    public IChannelResponseHook? ResponseHook { get; set; }
}
```

- A Responses request maps to a `ChannelRequest` (`Operation = "message.create"`, `Input` = parsed input
  items, `Session.Key` = `previous_response_id` when present, `Session.IsolationKey` derived by the channel
  or host middleware from a trusted source).
- The originating Responses response (and SSE stream) is rendered by the channel. Streaming serialization
  stays in the channel; the host applies `IChannelStreamTransformHook` as updates are consumed.
- For a `Workflow` target, the run hook prepares typed workflow input and the channel renders
  `RequestInfoEvent` as the protocol's awaiting-input shape; resume is caller-driven via the checkpoint id.

## E2E code samples

### Sample 1: Responses agent on one host

```csharp
using Microsoft.Agents.AI.Hosting.Channels.Responses;
using Microsoft.Extensions.Hosting;
using OpenAI.Chat;

var builder = WebApplication.CreateBuilder(args);

AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
    .GetChatClient(deployment)
    .AsAIAgent(name: "Concierge", instructions: "You are a helpful concierge.");

builder.AddAgentFrameworkHost(agent)
    .AddResponsesChannel();

var app = builder.Build();
app.MapAgentFrameworkHost();
app.Run();
```

### Sample 2: Responses-hosted workflow with run-hook input prep + checkpoints

```csharp
var workflow = new WorkflowBuilder(checkpointStorage: new FileCheckpointStorage("./.checkpoints"))
    .AddExecutor(/* intake */)
    .Build();

builder.AddAgentFrameworkHost(workflow, o => o.StatePaths = new HostStatePathOptions { Root = "./.afhost" })
    .AddResponsesChannel(o => o.RunHook = new MyWorkflowInputRunHook());

var app = builder.Build();
app.MapAgentFrameworkHost();
app.Run();
```

The run hook turns the parsed Responses input into the workflow's typed input. If the workflow pauses on a
`RequestInfoEvent`, the channel renders an awaiting-input response; the caller resumes by re-invoking with
`attributes["workflow.checkpoint_id"]`.

## Test strategy

| Layer | Test type | Proves |
|---|---|---|
| Channel contract | Unit | `Contribute` returns routes/commands/lifecycle; host aggregates them under `MapGroup(Path)`. |
| Host composition | Unit | `AddAgentFrameworkHost` + `AddResponsesChannel` produces a host whose `Channels` list matches; `ConfigureServices` runs pre-Build, `Contribute` post-Build. |
| Session continuity | Unit | Identical `IsolationKey` resolves to the same cached `AgentSession`; `ResetSessionAsync` rotates the alias. |
| `ResponsesChannel` | Integration (`TestServer`) | Responses request round-trips the full `ChatMessage` content list (no lossy collapse); SSE stream renders. |
| Workflow path | Integration | `RequestInfoEvent` renders awaiting-input; re-invoke with `workflow.checkpoint_id` resumes. |
| Isolation middleware | Unit (`TestServer`) | Headers lift into `IsolationKeys.Current` only under the Foundry flag; ignored otherwise. |

## Phasing

1. **Core** — `Microsoft.Agents.AI.Hosting.Channels`: every type in the core layout. `InMemoryHostStateStore`,
   `FileHostStateStore`, `AIAgentRunner`, `WorkflowRunner`. Unit tests per type.
2. **Responses channel** — `Microsoft.Agents.AI.Hosting.Channels.Responses` reusing Hosting.OpenAI Responses
   models. Integration tests for agent + workflow targets.
3. **Samples + docs** — the two samples above. README per package.

## Non-goals for v1 (deferred to ADR-0028)

Deliberately **not** part of this contract; tracked by
[ADR-0028](../decisions/0028-hosting-linking-multicast-enhancements.md):

- cross-channel identity linking (`IIdentityLinker`, one-time-code / Entra linkers),
- identity allowlists / authorization policy (`IIdentityAllowlist`, `AuthorizationProfile`),
- response routing beyond the originating channel (`ResponseTarget`, active channel, all-linked),
- push or payload codecs (`IChannelPush`, `IChannelPushCodec`),
- background runs + continuation tokens,
- durable task runners (`IDurableTaskRunner`, in-process runner),
- retry / replay (`RetryPolicy`),
- fan-out / multicast / all-linked delivery,
- confidentiality tiers and `ILinkPolicy`,
- multi-user conversation scoping / group addressing (`ConversationScope`, `AcceptInGroup`),
- additional channel packages (Invocations, Telegram, Discord, Activity Protocol),
- a host-level multi-agent router.

These are follow-up enhancements, not prerequisites for shipping or using the v1 host.

## References

- .NET ADRs (scope): [`0027-hosting-channels.md`](../decisions/0027-hosting-channels.md) (v1 core),
  [`0028-hosting-linking-multicast-enhancements.md`](../decisions/0028-hosting-linking-multicast-enhancements.md) (deferred).
- Python reference impl: PR microsoft/agent-framework#6580 (`agent-framework-hosting` + `agent-framework-hosting-responses`).
- Python spec: [`002-python-hosting-channels.md`](./002-python-hosting-channels.md).
- Existing .NET hosting packages this work coexists with: `Microsoft.Agents.AI.Hosting.OpenAI`,
  `Microsoft.Agents.AI.Hosting.A2A(.AspNetCore)`, `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`,
  `Microsoft.Agents.AI.Hosting.AzureFunctions`, `Microsoft.Agents.AI.Foundry.Hosting`.