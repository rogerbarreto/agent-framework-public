// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Converters;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Tests;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace Microsoft.Agents.AI.Hosting.OpenAI.UnitTests;

public sealed class ResponseCitationTests : ConformanceTestBase
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Response_PreservesCitationMetadataAsync(bool streaming, bool annotationOnly)
    {
        // Arrange
        List<AIAnnotation> annotations =
        [
            new CitationAnnotation
            {
                Url = new Uri("https://example.com/source"),
                Title = "Source",
                AnnotatedRegions =
                [
                    new TextSpanAnnotatedRegion { StartIndex = 0, EndIndex = 5 },
                    new TextSpanAnnotatedRegion { StartIndex = 6, EndIndex = 11 }
                ]
            },
            new CitationAnnotation { RawRepresentation = new FileCitationMessageAnnotation("file_123", 2, "report.pdf") },
            new CitationAnnotation { RawRepresentation = new FilePathMessageAnnotation("file_456", 3) },
            new CitationAnnotation
            {
                RawRepresentation = new ContainerFileCitationMessageAnnotation("container_1", "file_789", 6, 11, "chart.png")
            }
        ];
        HttpClient client = await this.CreateTestServerAsync("citations", "Answer with citations.", "Hello world",
            _ => annotationOnly
                ?
                [
                    new TextContent("Hello ") { Annotations = annotations.GetRange(0, 1) },
                    new TextContent("world"),
                    new TextContent(string.Empty) { Annotations = annotations.GetRange(1, annotations.Count - 1) }
                ]
                : [new TextContent("Hello world") { Annotations = annotations }]);

        // Act
        using HttpResponseMessage response = await this.SendResponsesRequestAsync(client, "citations",
            $$"""{"model":"test","input":"test","stream":{{(streaming ? "true" : "false")}}}""");
        string body = await response.Content.ReadAsStringAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        if (!streaming)
        {
            using JsonDocument document = JsonDocument.Parse(body);
            AssertAnnotations(document.RootElement.GetProperty("output")[0].GetProperty("content")[0].GetProperty("annotations"));
            return;
        }

        List<JsonElement> events = [];
        foreach (string line in body.Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal)))
        {
            using JsonDocument document = JsonDocument.Parse(line.Substring("data: ".Length));
            events.Add(document.RootElement.Clone());
        }
        JsonElement[] annotationEvents = events.Where(e => e.GetProperty("type").GetString() == "response.output_text.annotation.added").ToArray();
        Assert.Equal(5, annotationEvents.Length);
        for (int i = 0; i < annotationEvents.Length; i++)
        {
            Assert.Equal(i, annotationEvents[i].GetProperty("annotation_index").GetInt32());
            Assert.Equal(0, annotationEvents[i].GetProperty("content_index").GetInt32());
        }

        JsonElement partDone = events.Single(e => e.GetProperty("type").GetString() == "response.content_part.done");
        AssertAnnotations(partDone.GetProperty("part").GetProperty("annotations"));
        for (int i = 0; i < annotationEvents.Length; i++)
        {
            Assert.Equal(partDone.GetProperty("item_id").GetString(), annotationEvents[i].GetProperty("item_id").GetString());
            Assert.Equal(partDone.GetProperty("output_index").GetInt32(), annotationEvents[i].GetProperty("output_index").GetInt32());
            Assert.Equal(partDone.GetProperty("part").GetProperty("annotations")[i].GetRawText(),
                annotationEvents[i].GetProperty("annotation").GetRawText());
        }

        JsonElement itemDone = events.Single(e => e.GetProperty("type").GetString() == "response.output_item.done");
        AssertAnnotations(itemDone.GetProperty("item").GetProperty("content")[0].GetProperty("annotations"));
        JsonElement completed = events.Single(e => e.GetProperty("type").GetString() == "response.completed");
        AssertAnnotations(completed.GetProperty("response").GetProperty("output")[0].GetProperty("content")[0].GetProperty("annotations"));
        Assert.Equal("Hello world", partDone.GetProperty("part").GetProperty("text").GetString());
        Assert.Equal(Enumerable.Range(0, events.Count), events.Select(e => e.GetProperty("sequence_number").GetInt32()));
    }

    [Fact]
    public void ToItemContent_UnsupportedAnnotations_AreSkipped()
    {
        // Arrange
        var text = new TextContent("Hello")
        {
            Annotations =
            [
                new AIAnnotation(),
                new CitationAnnotation { Title = "No URL" },
                new CitationAnnotation { Url = new Uri("https://example.com/no-region") },
                new CitationAnnotation
                {
                    Url = new Uri("https://example.com/incomplete-region"),
                    AnnotatedRegions = [new TextSpanAnnotatedRegion { StartIndex = 0 }]
                }
            ]
        };

        // Act
        var output = Assert.IsType<ItemContentOutputText>(ItemContentConverter.ToItemContent(text));

        // Assert
        Assert.Empty(output.Annotations);
    }

    [Fact]
    public void ToItemContent_UrlCitationWithoutTitle_UsesEmptyTitle()
    {
        // Arrange
        var text = new TextContent("Hello")
        {
            Annotations =
            [
                new CitationAnnotation
                {
                    Url = new Uri("https://example.com/source"),
                    AnnotatedRegions = [new TextSpanAnnotatedRegion { StartIndex = 0, EndIndex = 5 }]
                }
            ]
        };

        // Act
        var output = Assert.IsType<ItemContentOutputText>(ItemContentConverter.ToItemContent(text));

        // Assert
        Assert.Equal(string.Empty, Assert.Single(output.Annotations).GetProperty("title").GetString());
    }

    [Fact]
    public void ToItemContent_RawUrlCitation_PreservesOriginalMetadata()
    {
        // Arrange
        var text = new TextContent("Hello")
        {
            Annotations =
            [
                new CitationAnnotation
                {
                    RawRepresentation = new UriCitationMessageAnnotation(new Uri("https://example.com/raw"), 1, 4, "Original")
                }
            ]
        };

        // Act
        var output = Assert.IsType<ItemContentOutputText>(ItemContentConverter.ToItemContent(text));

        // Assert
        JsonElement annotation = Assert.Single(output.Annotations);
        Assert.Equal("https://example.com/raw", annotation.GetProperty("url").GetString());
        Assert.Equal("Original", annotation.GetProperty("title").GetString());
        Assert.Equal(1, annotation.GetProperty("start_index").GetInt32());
        Assert.Equal(4, annotation.GetProperty("end_index").GetInt32());
    }

    private static void AssertAnnotations(JsonElement annotations)
    {
        Assert.Equal(5, annotations.GetArrayLength());
        Assert.Equal("url_citation", annotations[0].GetProperty("type").GetString());
        Assert.Equal("https://example.com/source", annotations[0].GetProperty("url").GetString());
        Assert.Equal("Source", annotations[0].GetProperty("title").GetString());
        Assert.Equal(0, annotations[0].GetProperty("start_index").GetInt32());
        Assert.Equal(5, annotations[0].GetProperty("end_index").GetInt32());
        Assert.Equal(6, annotations[1].GetProperty("start_index").GetInt32());
        Assert.Equal(11, annotations[1].GetProperty("end_index").GetInt32());
        Assert.Equal("file_citation", annotations[2].GetProperty("type").GetString());
        Assert.Equal("file_123", annotations[2].GetProperty("file_id").GetString());
        Assert.Equal(2, annotations[2].GetProperty("index").GetInt32());
        Assert.Equal("report.pdf", annotations[2].GetProperty("filename").GetString());
        Assert.Equal("file_path", annotations[3].GetProperty("type").GetString());
        Assert.Equal("file_456", annotations[3].GetProperty("file_id").GetString());
        Assert.Equal(3, annotations[3].GetProperty("index").GetInt32());
        Assert.Equal("container_file_citation", annotations[4].GetProperty("type").GetString());
        Assert.Equal("container_1", annotations[4].GetProperty("container_id").GetString());
        Assert.Equal("file_789", annotations[4].GetProperty("file_id").GetString());
        Assert.Equal("chart.png", annotations[4].GetProperty("filename").GetString());
        Assert.Equal(6, annotations[4].GetProperty("start_index").GetInt32());
        Assert.Equal(11, annotations[4].GetProperty("end_index").GetInt32());
    }
}
