// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Microsoft.Agents.AI.Hosting.OpenAI.UnitTests;

/// <summary>
/// Regression tests for session lifetime and ordering across Responses API requests.
/// </summary>
public sealed class ResponseSessionRegressionTests
{
    private const string AgentName = "session-regression-agent";
    private const string ToolName = "get_weather";

    [Fact]
    public async Task ApprovalContinuation_TransientAgent_UsesStableRegistrationIdentityAsync()
    {
        // Arrange
        int modelCalls = 0;
        int toolCalls = 0;
        AIFunction function = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            () =>
            {
                Interlocked.Increment(ref toolCalls);
                return "Sunny";
            },
            ToolName));
        Mock<IChatClient> chatClient = CreateApprovalChatClient(() => Interlocked.Increment(ref modelCalls));

        WebApplicationBuilder builder = CreateBuilder();
        builder.AddAIAgent(
                AgentName,
                (_, name) => new ChatClientAgent(chatClient.Object, name: name, tools: [function]),
                ServiceLifetime.Transient)
            .WithInMemorySessionStore(withIsolation: false);
        builder.AddOpenAIResponses();

        await using WebApplication app = builder.Build();
        app.MapOpenAIResponses();
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();

        (string responseId, JsonElement approvalEvent) = await CreatePendingApprovalAsync(
            client,
            "/v1/responses",
            includeAgentName: true);
        using StringContent approvalContent = JsonContent(CreateApprovalResponseJson(
            responseId,
            approvalEvent,
            includeAgentName: true));

        // Act
        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/v1/responses", UriKind.Relative),
            approvalContent);
        string responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.True(response.IsSuccessStatusCode, responseBody);
        Assert.Equal(1, toolCalls);
        Assert.Equal(2, modelCalls);
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var options = new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        };
        WebApplicationBuilder builder = WebApplication.CreateBuilder(options);
        builder.WebHost.UseTestServer();
        return builder;
    }

    private static Mock<IChatClient> CreateApprovalChatClient(Func<int> nextCall)
    {
        Mock<IChatClient> chatClient = new();
        chatClient
            .Setup(client => client.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                AIContent content = nextCall() == 1
                    ? new FunctionCallContent("call-1", ToolName)
                    : new TextContent("Decision processed");
                return new ChatResponse([
                    new ChatMessage(ChatRole.Assistant, [content])
                ]).ToChatResponseUpdates().ToAsyncEnumerable();
            });
        return chatClient;
    }

    private static async Task<(string ResponseId, JsonElement ApprovalEvent)> CreatePendingApprovalAsync(
        HttpClient client,
        string path,
        bool includeAgentName)
    {
        string agentProperty = includeAgentName
            ? $$""" "agent": { "name": "{{AgentName}}" },"""
            : string.Empty;
        using StringContent content = JsonContent($$"""
            {
              {{agentProperty}}
              "input": "hello",
              "stream": true
            }
            """);
        using HttpResponseMessage response = await client.PostAsync(
            new Uri(path, UriKind.Relative),
            content);
        response.EnsureSuccessStatusCode();

        List<JsonElement> events = ParseSseEvents(await response.Content.ReadAsStringAsync());
        JsonElement approvalEvent = Assert.Single(events,
            item => item.GetProperty("type").GetString() == "response.function_approval.requested");
        string responseId = events.Last().GetProperty("response").GetProperty("id").GetString()!;
        return (responseId, approvalEvent);
    }

    private static string CreateApprovalResponseJson(
        string responseId,
        JsonElement approvalEvent,
        bool includeAgentName)
    {
        string agentProperty = includeAgentName
            ? $$""" "agent": { "name": "{{AgentName}}" },"""
            : string.Empty;
        return $$"""
            {
              {{agentProperty}}
              "previous_response_id": {{JsonSerializer.Serialize(responseId)}},
              "input": [{
                "type": "message",
                "role": "user",
                "content": [{
                  "type": "function_approval_response",
                  "request_id": {{approvalEvent.GetProperty("request_id").GetRawText()}},
                  "approved": true,
                  "function_call": {{approvalEvent.GetProperty("function_call").GetRawText()}}
                }]
              }]
            }
            """;
    }

    private static List<JsonElement> ParseSseEvents(string content)
    {
        var events = new List<JsonElement>();
        foreach (string line in content.Split('\n'))
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line["data: ".Length..]);
            events.Add(document.RootElement.Clone());
        }

        return events;
    }

    private static StringContent JsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");

}
