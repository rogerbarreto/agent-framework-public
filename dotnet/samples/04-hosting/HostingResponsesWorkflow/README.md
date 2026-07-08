# Hosting Responses Workflow (app-owned routing + checkpoint resume)

Shows how an application can own its own ASP.NET Core route and expose a workflow over the OpenAI
Responses protocol, using the `OpenAIResponses` conversion helpers for the wire protocol and
`HostedWorkflowState` for per-session checkpoint resume.

The application owns routing, authentication, and checkpoint storage. `HostedWorkflowState` pairs the
workflow with a `CheckpointManager` (in-memory by default) and a per-session `sessionId -> CheckpointInfo`
head cursor, so `RunOrResumeAsync(sessionId, input)` runs the workflow forward on the first call for a
session and resumes from the session's last checkpoint thereafter.

> The .NET workflow checkpoint store is already keyed by session id, but `CheckpointInfo` carries no
> ordering, which is why the holder remembers the head checkpoint per session.

## Run

```bash
export AZURE_OPENAI_ENDPOINT="https://<your-resource>.openai.azure.com"
export AZURE_OPENAI_DEPLOYMENT="gpt-4o-mini"   # optional, defaults to gpt-4o-mini
dotnet run
```

Call the endpoint:

```bash
curl -s http://localhost:5000/responses -H "content-type: application/json" \
  -d '{ "input": "Draft a short product announcement." }'
```

The response JSON includes the recorded checkpoint id in its summary text. Send a follow-up request with
the same `previous_response_id` (or `conversation`) to resume from that checkpoint.

## Notes

- Real applications extract the workflow's output from the emitted `WorkflowEvent`s per their workflow's
  design; this sample summarizes the run for illustration.
- The default in-memory cursor does not survive process restarts. Durable or multi-replica hosts should
  supply a durable `CheckpointManager` and record `HostedWorkflowRunResult.Checkpoint` in their own
  durable cursor.

## Security note

`GetSessionId(...)` returns an untrusted candidate key. Authenticate the caller and authorize/bind the id
before using it as the workflow's checkpoint session id. The checkpoint boundary must be at least as
specific as the authorized session boundary.
