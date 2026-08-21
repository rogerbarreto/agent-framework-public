// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ClientModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Foundry.Hosting.IntegrationTests.Fixtures;
using OpenAI.Responses;

#pragma warning disable OPENAI001 // Experimental Responses API surfaces

namespace Foundry.Hosting.IntegrationTests;

/// <summary>
/// Live long-running and crash-recovery tests for resilient background Responses hosting.
/// </summary>
[Trait("Category", "FoundryHostedAgents")]
public sealed class ResilientWorkflowHostedAgentTests(ResilientWorkflowHostedAgentFixture fixture)
    : IClassFixture<ResilientWorkflowHostedAgentFixture>
{
    private static readonly TimeSpan s_completionTimeout = TimeSpan.FromMinutes(6);
    private readonly ResilientWorkflowHostedAgentFixture _fixture = fixture;

    [Fact]
    public async Task BackgroundResponse_ContinuesWithoutClientConnectionAsync()
    {
        // Arrange
        string token = Guid.NewGuid().ToString("N");
        CreateResponseOptions options = CreateBackgroundRequest($"long:{token}");
        var responses = this._fixture.AgentOpenAIClient.GetProjectResponsesClient();
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Act
        ResponseResult accepted = (await responses.CreateResponseAsync(options)).Value;
        TimeSpan acceptanceTime = stopwatch.Elapsed;

        // Leave the response alone while its deterministic delay runs.
        await Task.Delay(TimeSpan.FromSeconds(25));
        ResponseWaitResult waitResult = await WaitForTerminalAsync(responses, accepted.Id, s_completionTimeout);

        // Assert
        Assert.True(accepted.Status is ResponseStatus.Queued or ResponseStatus.InProgress);
        Assert.True(acceptanceTime < TimeSpan.FromSeconds(10), $"Background acceptance took {acceptanceTime}.");
        Assert.Equal(ResponseStatus.Completed, waitResult.Response.Status);
        Assert.Contains($"LONG-RUN-COMPLETE:{token}", waitResult.Response.GetOutputText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackgroundResponse_ProcessCrash_RecoversAndCompletesAsync()
    {
        // Arrange
        string token = Guid.NewGuid().ToString("N");
        CreateResponseOptions options = CreateBackgroundRequest($"crash:{token}");
        var responses = this._fixture.AgentOpenAIClient.GetProjectResponsesClient();
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Act
        ResponseResult accepted = (await responses.CreateResponseAsync(options)).Value;
        TimeSpan acceptanceTime = stopwatch.Elapsed;
        ResponseWaitResult waitResult = await WaitForTerminalAsync(responses, accepted.Id, s_completionTimeout);

        // Assert: this token is emitted only after a new process observes the crash marker written
        // immediately before Environment.Exit.
        Assert.True(accepted.Status is ResponseStatus.Queued or ResponseStatus.InProgress);
        Assert.True(
            waitResult.SawSessionNotReady
                || waitResult.SawResponseNotFound
                || waitResult.LongestPollDuration > acceptanceTime,
            "Expected recovery to return transient HTTP 424/404 or take longer than background acceptance. " +
            $"Acceptance: {acceptanceTime}; longest poll: {waitResult.LongestPollDuration}.");
        Assert.Equal(ResponseStatus.Completed, waitResult.Response.Status);
        Assert.Contains(
            $"CRASH-RECOVERED:{token}:PROCESS-CHANGED",
            waitResult.Response.GetOutputText(),
            StringComparison.Ordinal);
    }

    private static CreateResponseOptions CreateBackgroundRequest(string input)
    {
        CreateResponseOptions options = new()
        {
            BackgroundModeEnabled = true,
            StoredOutputEnabled = true,
        };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(input));
        return options;
    }

    private static async Task<ResponseWaitResult> WaitForTerminalAsync(
        ResponsesClient responses,
        string responseId,
        TimeSpan timeout)
    {
        bool sawSessionNotReady = false;
        bool sawResponseNotFound = false;
        TimeSpan longestPollDuration = TimeSpan.Zero;
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ResponseResult response;
            Stopwatch pollStopwatch = Stopwatch.StartNew();
            try
            {
                response = (await responses.GetResponseAsync(responseId)).Value;
            }
            catch (ClientResultException ex) when (ex.Status == 424)
            {
                longestPollDuration = Max(longestPollDuration, pollStopwatch.Elapsed);
                sawSessionNotReady = true;
                await Task.Delay(TimeSpan.FromSeconds(2));
                continue;
            }
            catch (ClientResultException ex) when (ex.Status == 404)
            {
                longestPollDuration = Max(longestPollDuration, pollStopwatch.Elapsed);
                sawResponseNotFound = true;
                await Task.Delay(TimeSpan.FromSeconds(2));
                continue;
            }

            longestPollDuration = Max(longestPollDuration, pollStopwatch.Elapsed);
            if (response.Status is ResponseStatus.Completed)
            {
                return new(
                    response,
                    sawSessionNotReady,
                    sawResponseNotFound,
                    longestPollDuration);
            }

            if (response.Status is ResponseStatus.Cancelled or ResponseStatus.Failed or ResponseStatus.Incomplete)
            {
                throw new InvalidOperationException(
                    $"Response '{responseId}' terminated with status '{response.Status}': {response.Error?.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new TimeoutException($"Response '{responseId}' did not complete within {timeout}.");

        static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;
    }

    private sealed record ResponseWaitResult(
        ResponseResult Response,
        bool SawSessionNotReady,
        bool SawResponseNotFound,
        TimeSpan LongestPollDuration);
}
