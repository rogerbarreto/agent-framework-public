# Using-E2E-Resilience

This local E2E runs
[`Hosted-Workflow-Resilient-Long-Running`](../Hosted-Workflow-Resilient-Long-Running/)
through two recovery scenarios:

1. `Crash`, which terminates the process abruptly.
2. `Shutdown`, which follows the graceful shutdown path, signals
   `ResponseContext.IsShutdownRequested`, and defers the response for recovery.

Each scenario uses isolated server state and starts a replacement process to continue the same
background response. Both scenarios execute the same client and verification logic. The only
difference is how the first server process stops.

## Idempotency behavior

Each countdown value calls a simulated idempotent service using a stable operation ID:

```text
11 | operation-id=<run-id>:11
```

The sample service keeps completed operation IDs in a `HashSet` and writes them to a small file under
the scenario's durable state directory. A replacement process reloads that file. The first call
performs the simulated effect and records the operation ID:

```text
Operation executed: <run-id>:11
```

If recovery executes the workflow step again, the service finds the existing operation ID and does
not perform the effect again:

```text
Duplicate operation ignored: <run-id>:11
```

The operation ID remains stable when the interrupted step runs again. Both scenarios deliberately
interrupt the step after its output is visible but before its workflow checkpoint completes. The raw
recovered stream therefore contains the operation ID twice:

```text
received > 11 | operation-id=<run-id>:11
received > 11 | operation-id=<run-id>:11
duplicate operation detected: <run-id>:11 (2 attempts)
```

This is expected at-least-once execution. A real email, payment, database write, or other external
effect can follow the same pattern: accept the stable operation ID as an idempotency key, perform the
effect only for the first request, and return the existing result for later attempts.

## What normally triggers each path

Foundry sends `SIGTERM` when it intentionally stops a hosted agent container and can provide a
graceful shutdown window. This can happen during managed lifecycle operations such as:

1. Session compute deprovisioning after the configured idle timeout.
2. Scale-in that removes a running container.
3. Redeployment that replaces the current container.

During this path, the container stops accepting new requests, finishes or defers in-flight work,
flushes pending writes, and closes connections. See the
[hosted agent runtime contract](https://learn.microsoft.com/azure/foundry/agents/concepts/hosted-agent-contract)
and [hosted agent lifecycle](https://learn.microsoft.com/azure/foundry/agents/concepts/hosted-agents).

An abrupt crash provides no shutdown window. Typical examples include an application process crash,
a forced process termination, and an out-of-memory kill. See
[resilience for long-running hosted agents](https://learn.microsoft.com/azure/foundry/agents/concepts/long-running-agent-resilience).

The `Shutdown` scenario closes its client replay connection and calls a local development endpoint
that invokes the AgentServer resilient task service's `StopAsync()` method before stopping the web
host. This reproduces the hosted-service shutdown mechanism used by the AgentServer unit tests as the
Windows equivalent of a production `SIGTERM`.

## Run

No Azure project, model deployment, credentials, or second terminal is required. The E2E builds the
server in Debug, uses random loopback ports, and stores each scenario's AgentServer state in an
isolated temporary directory.

From the repository root:

```powershell
dotnet run --project dotnet\samples\04-hosting\FoundryHostedAgents\responses\Using-E2E-Resilience
```

Options:

```powershell
dotnet run --project dotnet\samples\04-hosting\FoundryHostedAgents\responses\Using-E2E-Resilience -- `
    --target 30 `
    --interrupt-after-count 12 `
    --delay-seconds 1
```

| Option | Default | Meaning |
| --- | --- | --- |
| `--target` | `20` | First countdown value. Must be at least 2. |
| `--interrupt-after-count` | Half the target | Number of operations received before interruption. |
| `--delay-seconds` | `1` | Delay before each countdown operation. |
