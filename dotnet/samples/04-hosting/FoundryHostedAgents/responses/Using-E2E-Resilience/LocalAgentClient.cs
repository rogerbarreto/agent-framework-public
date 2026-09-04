// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;

internal sealed class LocalAgentClient(
    AIAgent agent,
    HttpClient transportClient) : IDisposable
{
    public AIAgent Agent { get; } = agent;

    public void Dispose() => transportClient.Dispose();
}
