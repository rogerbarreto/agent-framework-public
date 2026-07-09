# Hosting Responses Agent (app-owned routing)

Shows how an application can own its own ASP.NET Core route and expose an `AIAgent` over the OpenAI
Responses protocol by calling the Agent Framework `OpenAIResponses` conversion helpers, instead of using
the batteries-included `MapOpenAIResponses` server.

The application owns routing, authentication, and session storage. The framework provides only the
protocol conversion:

- `OpenAIResponses.GetSessionId(body)` extracts the untrusted continuation-id candidate.
- `OpenAIResponses.ToAgentRunRequest(body)` parses the request into messages + run options.
- `OpenAIResponses.WriteResponse(...)` / `WriteResponseStreamAsync(...)` render the agent output back to
  the Responses wire shape (non-streaming JSON and SSE).

Session continuity uses `HostedAgentState` over an in-memory `AgentSessionStore`.

## Run

```bash
export FOUNDRY_PROJECT_ENDPOINT="https://<your-resource>.services.ai.azure.com/api/projects/<your-project>"
export FOUNDRY_MODEL="gpt-5.4-mini"   # optional, defaults to gpt-5.4-mini
dotnet run
```

Call the endpoint (non-streaming):

```bash
curl -s http://localhost:5000/responses -H "content-type: application/json" \
  -d '{ "input": "Write a haiku about the sea." }'
```

Streaming (SSE):

```bash
curl -N http://localhost:5000/responses -H "content-type: application/json" \
  -d '{ "input": "Write a haiku about the sea.", "stream": true }'
```

## Security note

`GetSessionId(...)` returns an untrusted candidate key. This sample's `Authorize(...)` is a placeholder;
a real application must authenticate the caller and authorize/bind the id to the authenticated principal
before using it as a session key. For multi-user hosts, scope the store with
`IsolationKeyScopedAgentSessionStore`.
