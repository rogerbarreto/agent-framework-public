// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Hosting.OpenAI.UnitTests;

/// <summary>
/// Unit tests for the <see cref="OpenAIResponses"/> helper facade.
/// </summary>
public class OpenAIResponsesTests
{
    [Fact]
    public void ToAgentRunRequest_StringInput_ProducesUserMessage()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""{ "input": "Hello there" }""");

        // Act
        var request = OpenAIResponses.ToAgentRunRequest(doc.RootElement);

        // Assert
        var message = Assert.Single(request.Messages);
        Assert.Equal(ChatRole.User, message.Role);
        Assert.Equal("Hello there", message.Text);
        Assert.Null(request.Options);
    }

    [Fact]
    public void GetSessionId_PreviousResponseId_IsReturned()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""{ "input": "hi", "previous_response_id": "resp_abc" }""");

        // Act & Assert
        Assert.Equal("resp_abc", OpenAIResponses.GetSessionId(doc.RootElement));
    }

    [Fact]
    public void GetSessionId_ConversationId_IsReturned()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""{ "input": "hi", "conversation": "conv_xyz" }""");

        // Act & Assert
        Assert.Equal("conv_xyz", OpenAIResponses.GetSessionId(doc.RootElement));
    }

    [Fact]
    public void GetSessionId_NoContinuationKey_ReturnsNull()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""{ "input": "hi" }""");

        // Act & Assert
        Assert.Null(OpenAIResponses.GetSessionId(doc.RootElement));
    }

    [Fact]
    public void CreateResponseId_HasResponsePrefix()
    {
        // Act
        string id = OpenAIResponses.CreateResponseId();

        // Assert
        Assert.StartsWith("resp_", id);
    }

    [Fact]
    public void WriteResponse_RendersIdAndOutputText()
    {
        // Arrange
        var response = new AgentResponse(new ChatMessage(ChatRole.Assistant, "Hello from the agent"));
        const string ResponseId = "resp_test123";

        // Act
        JsonElement payload = OpenAIResponses.WriteResponse(response, ResponseId, sessionId: "conv_1");

        // Assert
        Assert.Equal(ResponseId, payload.GetProperty("id").GetString());
        Assert.Equal("conv_1", payload.GetProperty("conversation").GetProperty("id").GetString());
        Assert.Contains("Hello from the agent", payload.GetRawText());
    }
}
