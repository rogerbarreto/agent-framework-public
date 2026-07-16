# Server (Hosting Responses Agent)

Server half of the [Hosting Responses Agent](../README.md) sample.

Exposes an `AIAgent` over the OpenAI Responses protocol on a `POST /responses` route you write:

- `OpenAIResponses.ToAgentRunRequest(body)` parses the request into messages, run options, and the
  continuation ids.
- `OpenAIResponses.GetSessionStoreId(run)` reads the untrusted continuation-id candidate off the parsed
  request.
- `OpenAIResponses.WriteResponse(...)` / `WriteResponseStreamAsync(...)` render the agent output back to the
  Responses wire shape (non-streaming JSON and SSE).

Session continuity uses an in-memory `AgentSessionStore` directly. `GetSessionAsync(agent, id)` creates a
session on first use and returns an independent instance per call; the store does no internal locking, so a
route that runs concurrent turns against the same id owns any coordination it needs.
The agent has a deterministic `lookup_weather` tool. Binds to `http://localhost:5000` (override with
`ASPNETCORE_URLS`).

```bash
export FOUNDRY_PROJECT_ENDPOINT="https://<your-resource>.services.ai.azure.com/api/projects/<your-project>"
export FOUNDRY_MODEL="gpt-5.4-mini"
dotnet run
```

You can also call it directly with curl:

```bash
curl -s http://localhost:5000/responses -H "content-type: application/json" \
  -d '{ "input": "What is the weather in Tokyo?" }'

curl -N http://localhost:5000/responses -H "content-type: application/json" \
  -d '{ "input": "What is the weather in Tokyo?", "stream": true }'
```
