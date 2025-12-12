// Copyright (c) Microsoft. All rights reserved.

using System.ClientModel;
using System.Threading.Tasks;
using AgentConformance.IntegrationTests;
using Xunit.Sdk;

namespace AzureAI.IntegrationTests;

public class AIProjectClientChatClientAgentRunStreamingTests() : ChatClientAgentRunStreamingTests<AIProjectClientFixture>(() => new())
{
    [Fact(Skip = "No messages is not supported")]
    public override Task RunWithInstructionsAndNoMessageReturnsExpectedResultAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public override async Task RunWithImageContentWorksAsync()
    {
        try
        {
            await base.RunWithImageContentWorksAsync();
        }
        catch (ClientResultException crex)
        {
            // Server side error bugs are ignored as this test should work by design.
            if (crex.Status == 500)
            {
                throw SkipException.ForSkip("Skipping due to server side error.");
            }
        }
    }
}
