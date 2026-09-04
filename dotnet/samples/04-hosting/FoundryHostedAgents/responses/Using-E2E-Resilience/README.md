# Using-E2E-Resilience

This local E2E runs
[`Hosted-Workflow-Resilient-Long-Running`](../Hosted-Workflow-Resilient-Long-Running/)
through two recovery scenarios:

1. `Crash`, which terminates the process abruptly.
2. `Shutdown`, which follows the graceful shutdown path, signals
   `ResponseContext.IsShutdownRequested`, and defers the response for recovery.

Each scenario uses isolated server state and starts a replacement process to continue the same
background response. Both scenarios execute the same client logic. The only difference is how the
first server process stops.

## Idempotency behavior

The countdown executor uses SQLite as its idempotency database. Each count is the primary key:

```sql
CREATE TABLE countdown_operations (
    count_value INTEGER PRIMARY KEY,
    result TEXT NOT NULL
);
```

The first execution stores the result. If recovery runs the same count again, `INSERT OR IGNORE`
leaves the existing row unchanged and the service returns its stored result.

The client does not attempt to remove repeated stream updates. After the recovered stream completes,
it opens the same SQLite database and verifies that it contains exactly one row for every countdown
operation. With the default target, both the Crash and Shutdown scenarios must finish with 20 rows.

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

The `Shutdown` scenario calls a local development endpoint that invokes the AgentServer resilient
task service's `StopAsync()` method before stopping the web host. This reproduces the hosted-service
shutdown mechanism used by the AgentServer unit tests as the Windows equivalent of a production
`SIGTERM`.

## Run

No Azure project, model deployment, credentials, or second terminal is required. The E2E builds the
server in Debug, uses random loopback ports, and stores each scenario's AgentServer state and SQLite
database in an isolated temporary directory.

From the repository root:

```powershell
dotnet run --project dotnet\samples\04-hosting\FoundryHostedAgents\responses\Using-E2E-Resilience
```

Options:

```powershell
dotnet run --project dotnet\samples\04-hosting\FoundryHostedAgents\responses\Using-E2E-Resilience -- `
    --target 30 `
    --interrupt-after-count 12
```

| Option | Default | Meaning |
| --- | --- | --- |
| `--target` | `20` | First countdown value. Must be at least 2. |
| `--interrupt-after-count` | Half the target | Number of operations received before interruption. |
