---
status: proposed
contact: rogerbarreto
date: 2026-11-17
deciders: rogerbarreto, SergeyMenshykh, westey-m
consulted: []
informed: []
---

# Split .NET Foundry hosting into a protocol-neutral core and protocol packages

## Context and Problem Statement

`Microsoft.Agents.AI.Foundry.Hosting` has shipped as one preview package. Its public entry points,
`AddFoundryResponses` and `MapFoundryResponses`, serve the Foundry **Responses** container protocol, and the package
depends on `Azure.AI.AgentServer.Responses` and `Microsoft.Extensions.AI.OpenAI`.

A Foundry container can serve more than one protocol: the container image specification states that "protocols are
additive". An A2A integration is planned in `Microsoft.Agents.AI.Foundry.Hosting.A2A` (scaffolded in
[#10244](https://github.com/microsoft/agent-framework/pull/10244)) on top of `Azure.AI.AgentServer.A2A`
([Azure/azure-sdk-for-net#64283](https://github.com/Azure/azure-sdk-for-net/pull/64283)).

About half of the current package does not depend on the Responses protocol: the registration of the one agent a
container serves and its lifetimes, the Foundry session store, the workflow checkpoint store, the hosted user identity
checks, the Foundry Toolbox MCP sessions with OAuth consent detection, and the listen port configuration. The
per-request user id and call id these services need come from `Azure.AI.AgentServer.Core`, which every protocol SDK
shares.

How should the Foundry hosting packages be split so that each protocol package carries only its own protocol
dependencies, while the shared services exist exactly once?

## Decision Drivers

- A dependency belongs in the shared package only when more than one protocol package needs it.
- A container serves one agent. The agent registration, the session store, and the toolbox service must exist once even
  when the container maps several protocols.
- Foundry Toolboxes are a central hosted-agent feature. Every protocol package must offer them without an extra
  opt-in package.
- Existing Responses applications should keep compiling after they change one package reference.
- Implementation details should not become public API only so that sibling packages can share them.

## Considered Options

1. Keep one package and have the A2A package depend on it.
2. Keep `Microsoft.Agents.AI.Foundry.Hosting` as the Responses package and add a new shared package beside it.
3. Make `Microsoft.Agents.AI.Foundry.Hosting` the protocol-neutral core, move the Responses integration to
   `Microsoft.Agents.AI.Foundry.Hosting.Responses`, and build A2A in `Microsoft.Agents.AI.Foundry.Hosting.A2A`.
4. Option 3, with the Foundry Toolbox integration in a separate opt-in package.

### Option 1: one package

- Good, because nothing breaks for existing consumers.
- Bad, because every A2A application would carry the Responses SDK, the OpenAI converters, and the Responses handler.
- Bad, because `AddFoundryResponses` owns the hosted agent registration and throws when called twice, so a second
  protocol could not reuse it without reshaping the package anyway.

### Option 2: keep the name for Responses, add a shared package

- Good, because existing consumers keep the same package id.
- Bad, because the unqualified name `Microsoft.Agents.AI.Foundry.Hosting` would keep meaning "Responses", while the
  shared package and the A2A package would be named differently, which is confusing next to the Azure SDK layout
  (`Azure.AI.AgentServer.Core`, `.Responses`, `.A2A`).

### Option 3: protocol-neutral core and one package per protocol

- Good, because each protocol package carries only its protocol dependencies.
- Good, because the shared services, including toolboxes, exist once and every protocol gets them.
- Good, because the names mirror the Azure SDK packages the protocol packages build on.
- Bad, because existing consumers must add `Microsoft.Agents.AI.Foundry.Hosting.Responses`. The break cannot be bridged
  with type forwarding, since the core package cannot reference a protocol package.

### Option 4: option 3 with toolboxes in an opt-in package

- Good, because applications that do not use toolboxes would not carry `ModelContextProtocol`.
- Bad, because toolboxes apply to A2A as well (see below), so both protocol packages would need the toolbox package to
  keep the feature, which removes the dependency savings and only adds a package.
- Bad, because a hosted Responses agent installed from one package would lack a central Foundry feature.

## Decision Outcome

Chosen option: "Option 3: protocol-neutral core and one package per protocol", because it is the only option that
keeps every shared service in one place, gives every protocol the toolbox feature, and keeps protocol-specific
dependencies out of the other protocol packages.

### Package layout

| Package | Contains | Dependencies |
| --- | --- | --- |
| `Microsoft.Agents.AI.Foundry.Hosting` | Agent source and lifetimes, `FoundryAgentSessionStore`, `FoundryJsonCheckpointStore` and `WithFoundryCheckpointing`, hosted user id (`GetFoundryHostedUserId`), Foundry Toolbox service with per-run MCP sessions and consent detection, listen port configuration, `AddFoundryAgentSessionStore`, `AddFoundryToolboxService` | `Azure.AI.AgentServer.Core`, ASP.NET Core, `Microsoft.Agents.AI.Workflows`, `ModelContextProtocol`, `Azure.Identity` |
| `Microsoft.Agents.AI.Foundry.Hosting.Responses` | `AddFoundryResponses`, `MapFoundryResponses`, `FoundryResponsesServerOptions`, the response handler, input and output converters, stored-output checks, the protocol version filter, the hosted `User-Agent` policy | the core package, `Azure.AI.AgentServer.Responses`, `Microsoft.Extensions.AI.OpenAI` |
| `Microsoft.Agents.AI.Foundry.Hosting.A2A` (planned) | The A2A protocol adapter | the core package, `Microsoft.Agents.AI.Hosting.A2A`, `Azure.AI.AgentServer.A2A` |

### Dependency rule

A dependency is placed in the core package only when at least two protocol packages need it.

| Dependency | Responses needs it | A2A needs it | Placement |
| --- | --- | --- | --- |
| `Azure.AI.AgentServer.Core` | Yes | Yes | Core |
| `Microsoft.Agents.AI.Workflows` | Yes, for workflow recovery and conversion | Yes, already through `Microsoft.Agents.AI.Hosting` | Core |
| `ModelContextProtocol` | Yes, for toolboxes | Yes, for toolboxes | Core |
| `Azure.AI.AgentServer.Responses` | Yes | No | Responses |
| `Microsoft.Extensions.AI.OpenAI` | Yes, for the OpenAI item converters | Only for the hosted `User-Agent` segment | Responses |

### Toolboxes apply to A2A

- The Foundry tools integration specification describes the toolbox proxy as a container-side MCP contract under the
  protocol-neutral container specification. Only its Responses composition section and the consent output item are
  Responses-specific.
- `Azure.AI.AgentServer.Core` captures the platform call id (`x-agent-foundry-call-id`) for every request in
  `FoundryAgentRequestContext`, and its `FoundryCallIdHandler` names toolbox MCP as a destination.
- The `Azure.AI.AgentServer.A2A` documentation tells A2A applications to forward the call id on outbound Foundry calls.
- The A2A travel sample in
  [microsoft-foundry/foundry-samples#1119](https://github.com/microsoft-foundry/foundry-samples/pull/1119) runs on
  `Azure.AI.AgentServer.A2A` and calls the Foundry Toolbox `web_search` tool from A2A-hosted agents.

### Implementation choices

- **Namespace.** Responses types keep the `Microsoft.Agents.AI.Foundry.Hosting` namespace, so an application only
  changes its package reference.
- **Extension classes.** The core package keeps the original class names: `ServiceCollectionExtensions` and
  `HostApplicationBuilderExtensions` host `AddFoundryAgentSessionStore` and `AddFoundryToolboxService`. Each protocol
  package prefixes the same base names with its protocol, following `A2AEndpointRouteBuilderExtensions` and
  `AGUIEndpointRouteBuilderExtensions`: the Responses package uses `ResponsesServiceCollectionExtensions`,
  `ResponsesHostApplicationBuilderExtensions`, and `ResponsesEndpointRouteBuilderExtensions`. Two static classes with
  the same name in the same namespace and different assemblies would make the class name ambiguous for anyone who
  references both packages.
- **Agent registration.** A container serves one agent, and it may expose that agent over several protocols. Every
  protocol registration records its agent through an internal core registration. A later protocol registration that
  passes the same agent instance, or the same factory delegate with the same lifetime, reuses the first registration;
  one that passes a different agent throws before it changes anything. Hosting cannot tell whether two different
  factories create the same agent, so applications pass one factory delegate to every protocol. Registering the same
  protocol twice still throws. A public protocol-neutral registration, such as `AddFoundryHostedAgent`, can follow if
  protocols need to share a factory-created agent without repeating it.
- **Internal services.** The core package grants `InternalsVisibleTo` to its protocol packages instead of making the
  agent source, toolbox sessions, and consent state public. The same pattern already exists between
  `Microsoft.Agents.AI.Workflows` and `Microsoft.Agents.AI.DurableTask`. Shared source helpers, such as `Throw`, are
  injected only into the core package, because a second copy in a protocol package would conflict with the visible
  internal copy (`CS0436`).
- **Platform headers.** The toolbox handler takes the call id header name from `Azure.AI.AgentServer.Core`
  (`PlatformHeaders.FoundryCallId`) rather than from the Responses protocol filter.
- **Feature usage.** `foundry.toolbox` (52) stays in the core package. `foundry_hosting` (53) moves to the Responses
  package, which activates it from `AddFoundryResponses` and `MapFoundryResponses`. The Responses `FeatureIndex` lives in
  a child namespace to avoid the same `CS0436` conflict.
- **Unit tests.** The unit tests follow the same split: `Microsoft.Agents.AI.Foundry.Hosting.UnitTests` covers the
  core package and `Microsoft.Agents.AI.Foundry.Hosting.Responses.UnitTests` covers the Responses package. Helpers both
  need, such as the toolbox loopback server and the environment-variable test collections, live in
  `Microsoft.Agents.AI.Foundry.Hosting.TestUtilities` and are linked into each project, because every test assembly
  runs in its own process and needs its own copy of an xUnit collection definition.
- **Samples.** Hosted samples pin a published `AgentFrameworkVersion` whose `Microsoft.Agents.AI.Foundry.Hosting`
  package still contains the Responses integration, so their package references stay unchanged. Inside the repository,
  that reference maps to the Responses project. The local feed scripts pack the Responses package and rename the
  reference in their disposable copy. When the first release with this split ships, the samples bump their version and
  reference `Microsoft.Agents.AI.Foundry.Hosting.Responses`.

### Consequences

- Good, because an A2A application never carries the Responses SDK or the OpenAI converters.
- Good, because the session store, the checkpoint store, and toolboxes behave the same for every protocol.
- Bad, because this is a breaking change for existing consumers, who must add
  `Microsoft.Agents.AI.Foundry.Hosting.Responses`. Code that calls the Responses registration methods with static
  class syntax, such as `ServiceCollectionExtensions.AddFoundryResponses(services, agent)`, must use the new
  `Responses` class names. Extension method syntax keeps compiling.
- Bad, because packages that share internals must ship in lockstep. A newer core package with older protocol packages,
  or the reverse, is not supported.

### Migration

```diff
- <PackageReference Include="Microsoft.Agents.AI.Foundry.Hosting" Version="..." />
+ <PackageReference Include="Microsoft.Agents.AI.Foundry.Hosting.Responses" Version="..." />
```

No source change is needed when the application uses extension method syntax.

## Open Items

1. **A2A consent rendering.** The tools integration specification defines how to surface OAuth consent only for
   Responses. A2A probably needs its `auth-required` task state. Confirm the mapping with the Foundry specification
   owners before implementing it.
2. **Per-run tool hook for A2A.** The A2A request handler in `Microsoft.Agents.AI.Hosting.A2A` cannot add tools to a
   single run. The A2A package needs one, for example an agent wrapper that opens the toolboxes for each run.
3. **Identity source.** Shared services take the user id and call id from the protocol package, not from the ambient
   request context, because Responses recovery runs have no HTTP request and restore both values from saved state.
4. **Feature usage for A2A.** Register a new feature id when the A2A package activates.
5. **Lockstep versioning.** Before general availability, decide between exact version dependencies across the
   Foundry hosting packages and public extension points for the shared services.
6. **Remaining shared helpers.** Readiness route mapping, readiness check registration, and the telemetry source name
   still live in the Responses package. Move them to the core package when the A2A package needs them.
