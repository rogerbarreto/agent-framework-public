// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading.Tasks;
using Foundry.Hosting.IntegrationTests.Fixtures;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Foundry.Hosting.IntegrationTests;

/// <summary>
/// Proves a hosted turn is kept once.
/// </summary>
/// <remarks>
/// <para>
/// The AgentServer SDK's storage provider records every hosted turn around the container's handler,
/// and that record is the conversation the caller reads. The agent's own run inside the container
/// talks to its own service, and if that service is asked to keep the turn it writes a second copy of
/// the same exchange, on a trail of its own that nobody reads and nobody reconciles.
/// </para>
/// <para>
/// The container agent here is an ordinary Foundry <c>ChatClientAgent</c>, like the first hosted agent
/// sample. It is wrapped so that after the run it appends <c>DOWNSTREAM_ID=&lt;id&gt;</c> to the reply,
/// carrying whatever its own run left behind on the service. The test then goes looking for that id:
/// finding it means a second copy exists.
/// </para>
/// </remarks>
[Trait("Category", "FoundryHostedAgents")]
public sealed class HostedDownstreamStoreTests(DownstreamStoreHostedAgentFixture fixture) : IClassFixture<DownstreamStoreHostedAgentFixture>
{
    private const string IdPrefix = "DOWNSTREAM_ID=";
    private const string NoId = "none";

    private readonly DownstreamStoreHostedAgentFixture _fixture = fixture;

    [Fact]
    public async Task StoredTurn_LeavesNothingBehindOnTheAgentsOwnServiceAsync()
    {
        // Arrange
        var agent = this._fixture.Agent;
        var conversationId = await this._fixture.CreateConversationAsync();
        try
        {
            var options = new ChatClientAgentRunOptions(new ChatOptions { ConversationId = conversationId });

            // Act: one stored turn, the way any caller would send it.
            var response = await agent.RunAsync("Reply with the word 'ack'.", options: options);

            // Assert: the caller's conversation holds this turn, so it was recorded once already.
            var recorded = await this._fixture.ReadConversationMessagesAsync(conversationId);
            Assert.NotEmpty(recorded);

            // And the agent's own run left nothing behind that can be read back off the service. An id
            // that still resolves is a second copy of the very same turn.
            var downstreamId = ParseDownstreamId(response.Text);
            if (downstreamId is null)
            {
                return;
            }

            var found = await this._fixture.TryReadResponseAsync(downstreamId);
            Assert.True(
                found is null,
                $"The agent's own run left a second copy of the turn on the service, readable as '{downstreamId}'.");
        }
        finally
        {
            await this._fixture.DeleteConversationAsync(conversationId);
        }
    }

    [Fact]
    public async Task MultiTurn_LeavesNothingBehindOnTheAgentsOwnServiceAsync()
    {
        // Arrange: same contract across a multi-turn conversation, where every turn would add its own
        // second copy.
        var agent = this._fixture.Agent;
        var conversationId = await this._fixture.CreateConversationAsync();
        try
        {
            var options = new ChatClientAgentRunOptions(new ChatOptions { ConversationId = conversationId });

            // Act
            var first = await agent.RunAsync("Remember the number 73. Acknowledge briefly.", options: options);
            var second = await agent.RunAsync("What number did I just tell you?", options: options);

            // Assert: the conversation still works, so history is reaching the model.
            Assert.Contains("73", second.Text);

            foreach (var text in new[] { first.Text, second.Text })
            {
                var downstreamId = ParseDownstreamId(text);
                if (downstreamId is null)
                {
                    continue;
                }

                var found = await this._fixture.TryReadResponseAsync(downstreamId);
                Assert.True(
                    found is null,
                    $"The agent's own run left a second copy of the turn on the service, readable as '{downstreamId}'.");
            }
        }
        finally
        {
            await this._fixture.DeleteConversationAsync(conversationId);
        }
    }

    /// <summary>
    /// Reads the id the container reported, or <see langword="null"/> when the run left nothing behind.
    /// </summary>
    private static string? ParseDownstreamId(string? text)
    {
        Assert.False(string.IsNullOrWhiteSpace(text));

        var marker = text!.IndexOf(IdPrefix, StringComparison.Ordinal);
        Assert.True(marker >= 0, $"Expected the container to report '{IdPrefix}...' but got: {text}");

        var value = text[(marker + IdPrefix.Length)..].Trim();
        return value.Length == 0 || value.Equals(NoId, StringComparison.Ordinal) ? null : value;
    }
}
