// Copyright (c) Microsoft. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using AgentConformance.IntegrationTests;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AzureAI.IntegrationTests;

public class AIProjectClientAgentRunPreviousResponseTests() : RunTests<AIProjectClientFixture>(() => new())
{
    [Fact(Skip = "No messages is not supported")]
    public override Task RunWithNoMessageDoesNotFailAsync()
    {
        return Task.CompletedTask;
    }
}

public class AIProjectClientAgentRunConversationTests() : RunTests<AIProjectClientFixture>(() => new())
{
    public override Func<Task<AgentRunOptions?>> AgentRunOptionsFactory => async () =>
    {
        var conversationId = await this.Fixture.CreateConversationAsync();
        return new ChatClientAgentRunOptions(new() { ConversationId = conversationId });
    };

    [Fact(Skip = "No messages is not supported")]
    public override Task RunWithNoMessageDoesNotFailAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RunWithImageContentWorksAsync()
    {
        const string AgentInstructions = "You are a helpful agent that can analyze images";
        const string AgentName = "VisionAgent";

        var agent = await this.Fixture.CreateChatClientAgentAsync(name: AgentName, instructions: AgentInstructions);
        try
        {
            ChatMessage message = new(ChatRole.User, [
                new TextContent("What do you see in this image?"),
                new DataContent(File.ReadAllBytes("assets/walkway.jpg"), "image/jpeg")
            ]);

            var thread = agent.GetNewThread();
            var response = await agent.RunAsync(message, thread);

            var isImageDescriptionFoundResponse = await agent.RunAsync($"Respond with Yes or No:\n Does text below looks like the description of an image?\n {response.Text}");
            Assert.Contains("Yes", isImageDescriptionFoundResponse.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await this.Fixture.DeleteAgentAsync(agent);
        }
    }
}
