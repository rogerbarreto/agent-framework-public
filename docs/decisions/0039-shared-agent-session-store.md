---
status: proposed
contact: rogerbarreto
date: 2026-09-01
deciders: rogerbarreto
consulted: []
informed: []
---

# Shared AgentSessionStore abstraction

## Context and Problem Statement

.NET has two public `AgentSessionStore` abstract classes. `Microsoft.Agents.AI.Hosting` defines a store
whose lookup creates a session when no value exists. `Microsoft.Agents.AI.Foundry.Hosting` defines a store
whose lookup returns `null`, accepts an explicit user partition, and provides a separate convenience method
that creates a session when needed. The types cannot be used interchangeably, so storage integrations depend
on a specific hosting protocol package instead of the core agent abstractions.

## Decision Drivers

- One storage contract must work across all hosting packages.
- Storage implementations must depend only on `Microsoft.Agents.AI.Abstractions`.
- A lookup must distinguish a missing value from a stored value without creating state as a side effect.
- Every caller must explicitly decide whether the session is partitioned by user.
- Existing Foundry storage behavior and per-user isolation must remain unchanged.

## Considered Options

1. Promote the Foundry Hosting contract to `Microsoft.Agents.AI.Abstractions`.
2. Promote the conventional Hosting contract and adapt Foundry Hosting to it.
3. Add a third contract and keep adapters for both existing contracts.

## Decision Outcome

Chosen option: **Promote the Foundry Hosting contract to `Microsoft.Agents.AI.Abstractions`**.

`AgentSessionStore` moves to the `Microsoft.Agents.AI` namespace and keeps the Foundry Hosting behavior:

- The abstraction and every public implementation start as experimental under diagnostic `MAAI001`.
- `GetSessionAsync` returns `AgentSession?` and returns `null` when no session is stored.
- `GetOrCreateSessionAsync` performs the explicit lookup or creation operation.
- `SaveSessionAsync` and both lookup methods require a `string? userId` argument with no default value.
  A non-null value must not be empty or contain only whitespace.
- `DeleteSessionAsync` and service inspection are not part of the shared contract.

The duplicate types in `Microsoft.Agents.AI.Hosting` and `Microsoft.Agents.AI.Foundry.Hosting` are removed.
Both packages reference the shared type directly.

The conventional Hosting implementations adopt the same behavior. `IsolationKeyScopedAgentSessionStore`
passes the key from `AgentIsolationKeyProvider` as the `userId` argument while leaving `conversationId`
unchanged. The in-memory and Azure Blob stores return `null` for a missing session and partition saved
sessions by user. `AIHostAgent` uses `GetOrCreateSessionAsync` when it needs a ready session.

Azure Blob Storage hashes a tagged, length-prefixed encoding of `userId` and `conversationId` under a
version 2 path. This prevents a scoped session from sharing a blob with an unscoped conversation whose
identifier contains the old delimiter. Version 1 keys are not read because the package is still preview and
the version 1 format cannot distinguish those two cases safely.

## Consequences

Positive:

- Storage implementations can be shared by Foundry Hosting, conventional Hosting, and future protocols.
- Missing session handling is explicit and consistent.
- User isolation is represented by its own argument instead of being encoded into a conversation identifier.
- `Microsoft.Agents.AI.Abstractions` owns the contract alongside `AIAgent` and `AgentSession`.

Negative:

- This is a source-breaking change for implementations of the preview Hosting contract.
- Callers must pass `userId: null` explicitly when no user partition exists.
- Consumers that need deletion must use a storage-specific API until a separate shared deletion capability is defined.

## More Information

- [ADR-0031](0031-hosted-per-user-session-storage-isolation.md) defines the explicit user partition used by the promoted contract.
- [ADR-0032](0032-dotnet-hosting-protocol-helpers.md) records the previous conventional Hosting contract.
