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
using Shared.IntegrationTests;

namespace Foundry.Hosting.IntegrationTests;

/// <summary>
/// Round-trip integration test for the alpha <see cref="AgentSessionFiles"/> SDK against the
/// session-files Hosted-Files style scenario. Validates that the SDK can create a session,
/// upload, list, download and delete a file inside the per-session sandbox volume.
/// </summary>
[Trait("Category", "FoundryHostedAgents")]
public sealed class SessionFilesHostedAgentTests(SessionFilesHostedAgentFixture fixture) : IClassFixture<SessionFilesHostedAgentFixture>
{
    private const string FoundryFeaturesHeader = "Foundry-Features";
    private const string HostedAgentsFeatureValue = "HostedAgents=V1Preview,AgentEndpoints=V1Preview";

    private const string TestDataFileName = "contoso_q1_2026_report.txt";

    /// <summary>
    /// A token that appears verbatim in the test data file. Asserts that the round-tripped
    /// bytes are exactly what we uploaded.
    /// </summary>
    private const string ExpectedTokenInFile = "1,482.6";

    private readonly SessionFilesHostedAgentFixture _fixture = fixture;

    [Fact]
    public async Task UploadListDownloadAndDeleteAsync()
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

            long expectedBytes = new FileInfo(localPath).Length;

            // Act 1 — upload via the alpha AgentSessionFiles SDK
            SessionFileWriteResponse writeResponse = await sessionFiles.UploadSessionFileAsync(
                agentName: this._fixture.AgentName,
                sessionId: session.AgentSessionId,
                sessionStoragePath: TestDataFileName,
                localPath: localPath);

            Assert.Equal(expectedBytes, writeResponse.BytesWritten);

            // Act 2 — verify the file is visible in the session sandbox listing
            SessionDirectoryListResponse listing = await sessionFiles.GetSessionFilesAsync(
                agentName: this._fixture.AgentName,
                sessionId: session.AgentSessionId,
                sessionStoragePath: ".");

            Assert.Contains(
                listing.Entries,
                e => e.Name == TestDataFileName && !e.IsDirectory && e.Size == expectedBytes);

            // Act 3 — round-trip via download and assert the bytes match (deterministic token)
            string downloadPath = Path.Combine(Path.GetTempPath(), $"hosted-files-it-{Guid.NewGuid():N}.txt");
            try
            {
                await sessionFiles.DownloadSessionFileAsync(
                    agentName: this._fixture.AgentName,
                    sessionId: session.AgentSessionId,
                    sessionStoragePath: TestDataFileName,
                    localPath: downloadPath);

                string downloaded = File.ReadAllText(downloadPath);
                Assert.Contains(ExpectedTokenInFile, downloaded);
            }
            finally
            {
                if (File.Exists(downloadPath))
                {
                    File.Delete(downloadPath);
                }
            }

            // Act 4 — delete the file and confirm it is gone
            await sessionFiles.DeleteSessionFileAsync(
                agentName: this._fixture.AgentName,
                sessionId: session.AgentSessionId,
                path: TestDataFileName);

            SessionDirectoryListResponse listingAfter = await sessionFiles.GetSessionFilesAsync(
                agentName: this._fixture.AgentName,
                sessionId: session.AgentSessionId,
                sessionStoragePath: ".");

            Assert.DoesNotContain(listingAfter.Entries, e => e.Name == TestDataFileName);
        }
        finally
        {
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
