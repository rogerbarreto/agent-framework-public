# Hosting Responses Workflow (app-owned routing + checkpoint resume)

Shows how an application can own its own ASP.NET Core route and expose a workflow over the OpenAI
Responses protocol, using the `OpenAIResponses` conversion helpers for the wire protocol and
`HostedWorkflowState` for per-session checkpoint resume.

The application owns routing, authentication, and checkpoint storage. `HostedWorkflowState` pairs the
workflow with a `CheckpointManager` (in-memory by default) and a per-session `sessionId -> CheckpointInfo`
head cursor. `RunOrResumeAsync(sessionId, input)` runs the workflow forward on the first call for a
session; on later calls it restores the session's latest checkpoint to rehydrate accumulated state and
then runs the workflow forward with the **new turn's input** (for agent workflows a `TurnToken` drives
the turn). This mirrors the Python hosting host's restore-then-run semantics.

> The .NET workflow checkpoint store is already keyed by session id, but `CheckpointInfo` carries no
> ordering, which is why the holder remembers the head checkpoint per session.

## Run

```bash
export FOUNDRY_PROJECT_ENDPOINT="https://<your-resource>.services.ai.azure.com/api/projects/<your-project>"
export FOUNDRY_MODEL="gpt-5.4-mini"   # optional, defaults to gpt-5.4-mini
dotnet run
```

First turn — start a conversation:

```bash
curl -s http://localhost:5000/responses -H "content-type: application/json" \
  -d '{ "input": "Draft a short product announcement for a reusable coffee mug.", "conversation": "conv-1" }'
```

Follow-up turn — resume with new input using the **same** `conversation` id:

```bash
curl -s http://localhost:5000/responses -H "content-type: application/json" \
  -d '{ "input": "Rewrite it as a single punchy tagline.", "conversation": "conv-1" }'
```

The second turn restores the first turn's checkpoint and applies the new instruction, so the answer
builds on the earlier draft. Use a **stable** key: `conversation` stays constant across turns, whereas
`previous_response_id` changes every turn and is not a valid checkpoint key.

Streaming (SSE) — add `"stream": true` to stream the workflow's agent updates back over the Responses
Server-Sent-Events wire (works for both the first turn and resumed turns):

```bash
curl -N http://localhost:5000/responses -H "content-type: application/json" \
  -d '{ "input": "Draft a short slogan for a coffee mug.", "conversation": "conv-1", "stream": true }'
```

## Notes

- The sample renders the workflow's final assistant message from the emitted `WorkflowEvent`s; real
  applications extract output per their own workflow's design.
- The default in-memory cursor does not survive process restarts. Durable or multi-replica hosts should
  supply a durable `CheckpointManager` and record `HostedWorkflowRunResult.Checkpoint` in their own
  durable cursor.

## Security note

`GetSessionId(...)` returns an untrusted candidate key. Authenticate the caller and authorize/bind the id
before using it as the workflow's checkpoint session id. The checkpoint boundary must be at least as
specific as the authorized session boundary.
