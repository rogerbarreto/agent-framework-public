// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable AAIP001 // AgentSessionFiles is experimental
#pragma warning disable OPENAI001 // CreateResponseOptions is experimental
#pragma warning disable SCME0001 // CreateResponseOptions.Patch is for evaluation purposes

using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AgentConformance.IntegrationTests.Support;
using Azure.AI.Projects.Agents;
using Foundry.Hosting.IntegrationTests.Fixtures;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Shared.IntegrationTests;

namespace Foundry.Hosting.IntegrationTests;

/// <summary>
/// End-to-end test for the alpha <see cref="AgentSessionFiles"/> API paired with a hosted agent
/// that reads files from the per-session <c>$HOME</c> sandbox volume. Validates that a file
/// uploaded through the SDK is visible to the agent's container-side tools when the chat request
/// is pinned to the same <c>agent_session_id</c>.
/// </summary>
[Trait("Category", "FoundryHostedAgents")]
public sealed class SessionFilesHostedAgentTests(SessionFilesHostedAgentFixture fixture) : IClassFixture<SessionFilesHostedAgentFixture>
{
    private const string FoundryFeaturesHeader = "Foundry-Features";
    private const string HostedAgentsFeatureValue = "HostedAgents=V1Preview,AgentEndpoints=V1Preview";

    private const string TestDataFileName = "contoso_q1_2026_report.txt";
    private const string ExpectedTokenInFile = "1,482.6";

    private readonly SessionFilesHostedAgentFixture _fixture = fixture;

    [Fact]
    public async Task UploadAndAgentReadsFileAsync()
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

        string isolationKey = Guid.NewGuid().ToString("N");
        string sessionId = Guid.NewGuid().ToString("N");

        ProjectAgentSession session = await adminClient.CreateSessionAsync(
            agentName: this._fixture.AgentName,
            isolationKey: isolationKey,
            versionIndicator: new VersionRefIndicator(this._fixture.AgentVersion),
            agentSessionId: sessionId);

        try
        {
            session = await WaitForActiveAsync(adminClient, this._fixture.AgentName, session.AgentSessionId);
            Assert.Equal(AgentSessionStatus.Active, session.Status);

            // Act 1 — upload via the alpha AgentSessionFiles SDK
            SessionFileWriteResponse writeResponse = await sessionFiles.UploadSessionFileAsync(
                agentName: this._fixture.AgentName,
                sessionId: session.AgentSessionId,
                sessionStoragePath: TestDataFileName,
                localPath: localPath);

            long expectedBytes = new FileInfo(localPath).Length;
            Assert.Equal(expectedBytes, writeResponse.BytesWritten);

            // Act 2 — verify the file is visible in the session sandbox listing
            SessionDirectoryListResponse listing = await sessionFiles.GetSessionFilesAsync(
                agentName: this._fixture.AgentName,
                sessionId: session.AgentSessionId,
                sessionStoragePath: ".");

            Assert.Contains(
                listing.Entries,
                e => e.Name == TestDataFileName && !e.IsDirectory && e.Size == expectedBytes);

            // Act 3 — invoke the agent against the same agent_session_id and assert it reads the file.
            // agent_session_id is injected into the /responses request body via JsonPatch because
            // it is a Foundry extension not surfaced as a typed property on CreateResponseOptions.
            string sessionIdJson = $"\"{session.AgentSessionId}\"";
            var runOptions = new Microsoft.Agents.AI.ChatClientAgentRunOptions(new ChatOptions
            {
                RawRepresentationFactory = _ =>
                {
                    var crOptions = new CreateResponseOptions();
                    crOptions.Patch.Set("$.agent_session_id"u8, BinaryData.FromString(sessionIdJson));
                    return crOptions;
                }
            });

            var response = await this._fixture.Agent.RunAsync(
                $"Read {TestDataFileName} from $HOME and quote the headline total revenue figure verbatim, no commentary.",
                options: runOptions);

            // Assert: the agent's response contains the deterministic token from the test file.
            Assert.False(string.IsNullOrWhiteSpace(response.Text));
            Assert.Contains(ExpectedTokenInFile, response.Text);
        }
        finally
        {
            try
            {
                await sessionFiles.DeleteSessionFileAsync(
                    agentName: this._fixture.AgentName,
                    sessionId: session.AgentSessionId,
                    path: TestDataFileName);
            }
            catch
            {
                // Best-effort cleanup.
            }

            try
            {
                await adminClient.DeleteSessionAsync(
                    agentName: this._fixture.AgentName,
                    sessionId: session.AgentSessionId,
                    isolationKey: isolationKey);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private static async Task<ProjectAgentSession> WaitForActiveAsync(
        AgentAdministrationClient client,
        string agentName,
        string sessionId)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);
        ProjectAgentSession session = (await client.GetSessionAsync(agentName, sessionId)).Value;
        while (session.Status != AgentSessionStatus.Active && session.Status != AgentSessionStatus.Failed)
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"Session '{sessionId}' did not become Active within 120s. Last status: {session.Status}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
            session = (await client.GetSessionAsync(agentName, sessionId)).Value;
        }

        return session;
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
