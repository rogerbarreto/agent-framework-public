// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable AAIP001 // AgentSessionFiles is experimental

using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AgentConformance.IntegrationTests.Support;
using Azure.AI.Projects.Agents;
using Foundry.Hosting.IntegrationTests.Fixtures;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Shared.IntegrationTests;

namespace Foundry.Hosting.IntegrationTests;

/// <summary>
/// End-to-end integration test for the Hosted-Files style scenario: a file uploaded by the client
/// via the alpha <see cref="AgentSessionFiles"/> SDK is read by the deployed hosted agent's tools
/// and surfaces in <see cref="AIAgent.RunAsync(string, AgentSession, AgentRunOptions, System.Threading.CancellationToken)"/>.
/// </summary>
[Trait("Category", "FoundryHostedAgents")]
public sealed class SessionFilesHostedAgentTests(SessionFilesHostedAgentFixture fixture) : IClassFixture<SessionFilesHostedAgentFixture>
{
    private const string FoundryFeaturesHeader = "Foundry-Features";
    private const string HostedAgentsFeatureValue = "HostedAgents=V1Preview,AgentEndpoints=V1Preview";

    private const string TestDataFileName = "contoso_q1_2026_report.txt";

    /// <summary>
    /// A token that appears verbatim in the test data file. The agent must quote it after reading
    /// the file from its $HOME volume — that is the proof the upload flowed through to inference.
    /// </summary>
    private const string ExpectedTokenInFile = "1,482.6";

    private readonly SessionFilesHostedAgentFixture _fixture = fixture;

    /// <summary>
    /// End-to-end flow: upload a file via the alpha <see cref="AgentSessionFiles"/> SDK and
    /// invoke the hosted agent via <see cref="AIAgent.RunAsync(string, AgentSession, AgentRunOptions, System.Threading.CancellationToken)"/>;
    /// assert that the agent's container-side <c>ReadFile</c> tool surfaced the uploaded
    /// content into the response.
    /// </summary>
    /// <remarks>
    /// Currently skipped: the Foundry alpha service returns HTTP 400 conflict on any
    /// <c>/responses</c> request that links to a prior session (via <c>previous_response_id</c>,
    /// <c>conversation_id</c>, or <c>agent_session_id</c> pinning). Without that link we cannot
    /// route the second invocation to the same per-session container the file was uploaded to,
    /// so the assertion is unreachable until the platform regression is resolved.
    /// </remarks>
    [Fact(Skip = "Pending Foundry platform fix: HTTP 400 conflict on /responses chained continuation against tool-using hosted agents.")]
    public async Task UploadedFile_IsReadByHostedAgentAsync()
    {
        // Arrange
        string localPath = Path.Combine(AppContext.BaseDirectory, "TestData", TestDataFileName);
        Assert.True(
            File.Exists(localPath),
            $"Test data file not found at '{localPath}'. Confirm the linked Content entry in the csproj.");

        var endpoint = new Uri(TestConfiguration.GetRequiredValue(TestSettings.AzureAIProjectEndpoint));
        var credential = TestAzureCliCredentials.CreateAzureCliCredential();

        var adminOptions = new AgentAdministrationClientOptions();
        adminOptions.AddPolicy(new FoundryFeaturesPolicy(HostedAgentsFeatureValue), PipelinePosition.PerCall);
        var adminClient = new AgentAdministrationClient(endpoint, credential, adminOptions);
        var sessionFiles = adminClient.GetAgentSessionFiles();

        var agent = this._fixture.Agent;

        // Step 1 — warm-up call without a session so the platform provisions a fresh per-session
        // container with a random agent_session_id. Capture the response id so we can chain the
        // next call via previous_response_id (which forces deterministic routing back to the
        // same session container per the Foundry session-derivation spec).
        var warmup = await agent.RunAsync("Reply with the single word 'ready' and nothing else.");
        Assert.False(string.IsNullOrWhiteSpace(warmup.Text));
        Assert.False(string.IsNullOrWhiteSpace(warmup.ResponseId), "Expected warm-up to surface a response id.");

        // Step 2 — resolve the agent_session_id the platform just created. The newest active
        // session for this agent is the one our warm-up call provisioned.
        string agentSessionId = await ResolveLatestSessionIdAsync(adminClient, this._fixture.AgentName)
            ?? throw new InvalidOperationException(
                $"No sessions found for agent '{this._fixture.AgentName}' after warm-up.");

        // Step 3 — upload the file via the alpha AgentSessionFiles SDK to that same session.
        SessionFileWriteResponse writeResponse = await sessionFiles.UploadSessionFileAsync(
            agentName: this._fixture.AgentName,
            sessionId: agentSessionId,
            sessionStoragePath: TestDataFileName,
            localPath: localPath);

        long expectedBytes = new FileInfo(localPath).Length;
        Assert.Equal(expectedBytes, writeResponse.BytesWritten);

        // Sanity check — the file is visible inside the same session sandbox.
        SessionDirectoryListResponse listing = await sessionFiles.GetSessionFilesAsync(
            agentName: this._fixture.AgentName,
            sessionId: agentSessionId,
            sessionStoragePath: ".");

        Assert.Contains(
            listing.Entries,
            e => e.Name == TestDataFileName && !e.IsDirectory && e.Size == expectedBytes);

        // Step 4 — invoke the agent again, chained to the warm-up via previous_response_id. The
        // platform routes the chained request to the same agent_session_id, so the agent's
        // container-side ReadFile tool sees the file we just uploaded.
        string warmupResponseId = warmup.ResponseId!;
        var chainedOptions = new ChatClientAgentRunOptions(new ChatOptions
        {
            RawRepresentationFactory = _ => new CreateResponseOptions { PreviousResponseId = warmupResponseId },
        });

        var response = await agent.RunAsync(
            $"Read {TestDataFileName} from $HOME and quote the headline total revenue figure verbatim, no commentary.",
            options: chainedOptions);

        // Assert: the response contains the deterministic token — proves the agent read the file.
        Assert.False(string.IsNullOrWhiteSpace(response.Text));
        Assert.Contains(ExpectedTokenInFile, response.Text);

        // Best-effort cleanup of the uploaded file. The session itself is left for TTL expiry
        // because it was provisioned by the platform (no isolation key is held by this test).
        try
        {
            await sessionFiles.DeleteSessionFileAsync(
                agentName: this._fixture.AgentName,
                sessionId: agentSessionId,
                path: TestDataFileName);
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static async Task<string?> ResolveLatestSessionIdAsync(
        AgentAdministrationClient client,
        string agentName)
    {
        await foreach (ProjectAgentSession s in client.GetSessionsAsync(
            agentName: agentName,
            limit: 1,
            order: AgentListOrder.Descending))
        {
            return s.AgentSessionId;
        }

        return null;
    }

    private sealed class FoundryFeaturesPolicy(string features) : PipelinePolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            this.SetHeader(message);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            this.SetHeader(message);
            await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        }

        private void SetHeader(PipelineMessage message)
        {
            message.Request.Headers.Remove(FoundryFeaturesHeader);
            message.Request.Headers.Add(FoundryFeaturesHeader, features);
        }
    }
}
