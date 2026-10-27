---
status: proposed
contact: rogerbarreto
date: 2026-10-22
deciders: rogerbarreto, eavanvalkenburg, westey-m
---

# Let the Copilot agent own runtime connection configuration

## Context and Problem Statement

The Invocations host passes application-parsed options to agents. For the Copilot
agent, provider settings, MCP settings, custom-agent MCP settings, session
credentials, authentication controls, and discovery roots are SDK session
configuration. Forwarding unfiltered request options can therefore change the
connections selected for a session.

The same option names can have different meanings for other agent implementations.
An Invocations-wide denylist also rejects connection settings produced by trusted
application parsers and option hooks. The compatibility concern is tracked in
[PR #9611](https://github.com/microsoft/agent-framework/pull/9611).

## Decision Drivers

- Keep application-owned connections protected by default.
- Preserve deliberate runtime connection configuration through explicit opt-in.
- Keep ordinary per-run generation options and agent defaults available.
- Apply the policy to direct calls and hosting without provider-specific host coupling.

## Considered Options

- Reserve connection names in Invocations. This rejects input early, but changes
  every hosted agent's contract and also blocks trusted parser and hook output.
- Disable all runtime options. This is simple, but unnecessarily removes model
  and timeout overrides.
- Keep runtime connections enabled by default. This preserves compatibility, but
  leaves applications forwarding unfiltered options without default protection.
- Make the Copilot agent reject runtime connections by default with constructor
  opt-in. This scopes the policy to the component interpreting the configuration,
  but still requires an explicit migration for existing Copilot runtime callers.

## Decision Outcome

Choose the agent-owned policy. Both `GitHubCopilotAgent` and
`RawGitHubCopilotAgent` expose `allow_runtime_connection_overrides=False`.
They reject per-run provider configuration, MCP configuration, session credentials,
authentication controls, and discovery roots, including empty or null values, before
SDK startup and again before assembling SDK session configuration. Application
`default_options` remain allowed, runtime custom agents without MCP configuration
remain supported, and ordinary runtime options retain their existing behavior. The
constructor flag is never read from run options or forwarded to the SDK.

The agents expose a side-effect-free `validate_run_options` preflight. Invocations
uses it after resolving the request-scoped agent and before accessing session storage.
Direct runs still validate at entry, and session assembly validates again.

Trusted applications may opt in to preserve runtime connection precedence over
defaults. This is an intentional behavioral compatibility change: existing callers
must move connections into defaults or enable the constructor flag.

The package bounds `github-copilot-sdk` below 1.0.26 because 1.0.25 is the latest
reviewed session configuration surface. Widening the bound requires classifying new
`create_session` and `resume_session` parameters and adding regression coverage before
publication.

Invocations no longer reserves these connection names for every agent. It reports
the existing `AgentInvalidRequestException` as a sanitized HTTP 400 or typed SSE
client error, while retaining generic provider-failure sanitization and legacy wire
behavior. Other agents retain responsibility for their own option semantics.
An updated host alone does not protect older Copilot package versions; release and
deployment must include the updated agent policy.

The proposed deciders include Python code owners; maintainer and engineering-management
approval is still required.
