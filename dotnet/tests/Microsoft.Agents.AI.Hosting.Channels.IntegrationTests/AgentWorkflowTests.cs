// Copyright (c) Microsoft. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.Channels.IntegrationTests.Support;
using Microsoft.Agents.AI.Hosting.Channels.Responses;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Agents.AI.Hosting.Channels.IntegrationTests;

/// <summary>
/// Verifies an agent workflow (AgentWorkflowBuilder.BuildSequential over multiple agents) hosted through the
/// Responses channel: the run hook adapts the parsed Responses input into the workflow's ChatMessage-list
/// input, the host's WorkflowRunner drives it, and a completed response is rendered over POST /responses.
/// No live model.
/// </summary>
public class AgentWorkflowTests
{
    [Fact]
    public async Task SequentialAgentWorkflow_RunsThroughResponsesChannelAsync()
    {
        // Arrange - two deterministic agents chained sequentially
        var workflow = AgentWorkflowBuilder.BuildSequential(new EchoAgent(), new FakeChatAgent());

        await using var app = await TestHostApp.StartAsync(b => b
            .AddAgentFrameworkHost(workflow)
            .AddResponsesChannel(o => o.RunHook = new ChatMessageListRunHook()));

        // Act
        var response = await app.Client.PostAsync(
            new System.Uri("http://localhost/responses"),
            Json("{ \"input\": \"hello\" }"));
        var body = await response.Content.ReadAsStringAsync();

        // Assert - the workflow ran to completion and rendered a Responses object
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("completed", doc.RootElement.GetProperty("status").GetString());
    }

    private static StringContent Json(string json) => new(json, System.Text.Encoding.UTF8, "application/json");
}
