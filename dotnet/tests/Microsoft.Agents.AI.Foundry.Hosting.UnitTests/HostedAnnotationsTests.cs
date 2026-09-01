// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ContainerFileCitationMessageAnnotation = OpenAI.Responses.ContainerFileCitationMessageAnnotation;
using FileCitationMessageAnnotation = OpenAI.Responses.FileCitationMessageAnnotation;
using FilePathMessageAnnotation = OpenAI.Responses.FilePathMessageAnnotation;

#pragma warning disable OPENAI001 // Experimental Responses API surfaces

namespace Microsoft.Agents.AI.Foundry.Hosting.UnitTests;

/// <summary>End-to-end annotation tests for the hosted Responses HTTP endpoint.</summary>
public sealed class HostedAnnotationsTests
{
    [Fact]
    public async Task ResponsesEndpoint_StreamTrue_EmitsAllAnnotationKindsAsync()
    {
        // Arrange and Act
        var (statusCode, mediaType, body) = await InvokeResponsesEndpointAsync(stream: true);

        // Assert
        Assert.Equal(HttpStatusCode.OK, statusCode);
        Assert.Equal("text/event-stream", mediaType);

        var events = await ParseSseEventsAsync(body);
        var annotationEvents = events
            .Where(e => e.GetProperty("type").GetString() == "response.output_text.annotation.added")
            .Select(e => e.GetProperty("annotation"))
            .ToArray();
        AssertAnnotations(annotationEvents);

        var contentPartDone = Assert.Single(events, e => e.GetProperty("type").GetString() == "response.content_part.done");
        AssertAnnotations(contentPartDone.GetProperty("part"));

        var outputItemDone = Assert.Single(events, e => e.GetProperty("type").GetString() == "response.output_item.done");
        var outputText = Assert.Single(outputItemDone.GetProperty("item").GetProperty("content").EnumerateArray());
        AssertAnnotations(outputText);

        var completed = Assert.Single(events, e => e.GetProperty("type").GetString() == "response.completed");
        Assert.Equal("response.completed", events[^1].GetProperty("type").GetString());
        var completedOutputItem = Assert.Single(completed.GetProperty("response").GetProperty("output").EnumerateArray());
        var completedOutputText = Assert.Single(completedOutputItem.GetProperty("content").EnumerateArray());
        AssertAnnotations(completedOutputText);
    }

    [Fact]
    public async Task ResponsesEndpoint_StreamFalse_ReturnsAllAnnotationKindsAsync()
    {
        // Arrange and Act
        var (statusCode, mediaType, body) = await InvokeResponsesEndpointAsync(stream: false);

        // Assert
        Assert.Equal(HttpStatusCode.OK, statusCode);
        Assert.Equal("application/json", mediaType);

        using var document = JsonDocument.Parse(body);
        var outputItem = Assert.Single(document.RootElement.GetProperty("output").EnumerateArray());
        var outputText = Assert.Single(outputItem.GetProperty("content").EnumerateArray());
        AssertAnnotations(outputText);
    }

    private static async Task<(HttpStatusCode StatusCode, string? MediaType, string Body)> InvokeResponsesEndpointAsync(bool stream)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        AIAgent agent = new ChatClientAgent(CreateAnnotationChatClient());
        builder.Services.AddFoundryResponses(agent, new InMemoryAgentSessionStore());
        builder.Services.AddSingleton<HostedSessionIsolationKeyProvider>(new FakeHostedSessionIsolationKeyProvider());
        builder.Services.AddLogging();

        await using var app = builder.Build();
        app.MapFoundryResponses();
        await app.StartAsync();

        var testServer = app.Services.GetRequiredService<IServer>() as TestServer
            ?? throw new InvalidOperationException("TestServer not found");
        using var client = testServer.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/responses")
        {
            Content = new StringContent(CreateRequestJson(stream), Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, body);
    }

    private static IChatClient CreateAnnotationChatClient()
    {
        var annotations = new AIAnnotation[]
        {
            new CitationAnnotation
            {
                Url = new Uri("https://example.com/doc"),
                Title = "Example Document",
                AnnotatedRegions = [new TextSpanAnnotatedRegion { StartIndex = 0, EndIndex = 5 }]
            },
            new CitationAnnotation
            {
                FileId = "file_1",
                Title = "report.pdf",
                RawRepresentation = new FileCitationMessageAnnotation("file_1", 1, "report.pdf")
            },
            new CitationAnnotation
            {
                FileId = "file_2",
                RawRepresentation = new FilePathMessageAnnotation("file_2", 2)
            },
            new CitationAnnotation
            {
                FileId = "file_3",
                Title = "chart.png",
                AnnotatedRegions = [new TextSpanAnnotatedRegion { StartIndex = 6, EndIndex = 11 }],
                RawRepresentation = new ContainerFileCitationMessageAnnotation(
                    "container_1",
                    "file_3",
                    6,
                    11,
                    "chart.png")
            },
        };

        var client = new Mock<IChatClient>();
        client.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => ToAsyncEnumerableUpdatesAsync(
                new ChatResponseUpdate(ChatRole.Assistant, "Hello sources") { MessageId = "msg_response" },
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new AIContent { Annotations = annotations }])
                {
                    MessageId = "msg_response"
                }));
        return client.Object;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ToAsyncEnumerableUpdatesAsync(
        params ChatResponseUpdate[] updates)
    {
        foreach (var update in updates)
        {
            yield return update;
        }

        await Task.CompletedTask;
    }

    private static string CreateRequestJson(bool stream) => $$"""
        {
          "model": "test",
          "stream": {{(stream ? "true" : "false")}},
          "input": [
            {
              "type": "message",
              "id": "msg_request",
              "status": "completed",
              "role": "user",
              "content": [{ "type": "input_text", "text": "Hello" }]
            }
          ]
        }
        """;

    private static async Task<List<JsonElement>> ParseSseEventsAsync(string body)
    {
        var events = new List<JsonElement>();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        await foreach (var item in SseParser.Create(stream).EnumerateAsync())
        {
            if (item.Data == "[DONE]")
            {
                continue;
            }

            using var document = JsonDocument.Parse(item.Data);
            Assert.Equal(document.RootElement.GetProperty("type").GetString(), item.EventType);
            events.Add(document.RootElement.Clone());
        }

        return events;
    }

    private static void AssertAnnotations(JsonElement outputText) =>
        AssertAnnotations(outputText.GetProperty("annotations").EnumerateArray().ToArray());

    private static void AssertAnnotations(IReadOnlyCollection<JsonElement> annotations)
    {
        Assert.Collection(
            annotations,
            annotation =>
            {
                Assert.Equal("url_citation", annotation.GetProperty("type").GetString());
                Assert.Equal("https://example.com/doc", annotation.GetProperty("url").GetString());
                Assert.Equal("Example Document", annotation.GetProperty("title").GetString());
                Assert.Equal(0, annotation.GetProperty("start_index").GetInt64());
                Assert.Equal(5, annotation.GetProperty("end_index").GetInt64());
            },
            annotation =>
            {
                Assert.Equal("file_citation", annotation.GetProperty("type").GetString());
                Assert.Equal("file_1", annotation.GetProperty("file_id").GetString());
                Assert.Equal(1, annotation.GetProperty("index").GetInt64());
                Assert.Equal("report.pdf", annotation.GetProperty("filename").GetString());
            },
            annotation =>
            {
                Assert.Equal("file_path", annotation.GetProperty("type").GetString());
                Assert.Equal("file_2", annotation.GetProperty("file_id").GetString());
                Assert.Equal(2, annotation.GetProperty("index").GetInt64());
            },
            annotation =>
            {
                Assert.Equal("container_file_citation", annotation.GetProperty("type").GetString());
                Assert.Equal("container_1", annotation.GetProperty("container_id").GetString());
                Assert.Equal("file_3", annotation.GetProperty("file_id").GetString());
                Assert.Equal(6, annotation.GetProperty("start_index").GetInt64());
                Assert.Equal(11, annotation.GetProperty("end_index").GetInt64());
                Assert.Equal("chart.png", annotation.GetProperty("filename").GetString());
            });
    }
}
