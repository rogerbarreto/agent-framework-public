// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Extensions.AI;

#pragma warning disable OPENAI001, CS0618

namespace Microsoft.Agents.AI.Foundry.UnitTests;

/// <summary>
/// Unit tests for the internal <see cref="FoundryChatClient"/>. Covers the three construction
/// modes (pure responses, server-side agent reference, hosted agent endpoint), the GetService
/// returns per mode, the metadata-tagging contract, the agent-framework user-agent registration,
/// the mode-3 URL parsing happy and error paths, and end-to-end behavior through the public
/// <c>AsAIAgent(AgentReference)</c> extension that constructs a FoundryChatClient internally.
/// </summary>
public sealed class FoundryChatClientTests
{
    #region Mode 1: pure responses (AIProjectClient + modelId)

    [Fact]
    public void Mode1_PureResponses_StampsFoundryProviderName()
    {
        // Arrange
        var projectClient = CreateProjectClient();

        // Act
        var chatClient = new FoundryChatClient(projectClient, "gpt-4o-mini");

        // Assert
        var metadata = chatClient.GetService<ChatClientMetadata>();
        Assert.NotNull(metadata);
        Assert.Equal("microsoft.foundry", metadata!.ProviderName);
        Assert.Equal("gpt-4o-mini", metadata.DefaultModelId);
    }

    [Fact]
    public void Mode1_PureResponses_ExposesAIProjectClient_ViaGetService()
    {
        // Arrange
        var projectClient = CreateProjectClient();

        // Act
        var chatClient = new FoundryChatClient(projectClient, "gpt-4o-mini");

        // Assert
        Assert.Same(projectClient, chatClient.GetService<AIProjectClient>());
        Assert.NotNull(chatClient.GetService<ProjectOpenAIClient>());
    }

    [Fact]
    public void Mode1_PureResponses_ReturnsNullForAgentSpecificServices()
    {
        // Arrange
        var projectClient = CreateProjectClient();

        // Act
        var chatClient = new FoundryChatClient(projectClient, "gpt-4o-mini");

        // Assert
        Assert.Null(chatClient.GetService<AgentReference>());
        Assert.Null(chatClient.GetService<ProjectsAgentVersion>());
        Assert.Null(chatClient.GetService<ProjectsAgentRecord>());
    }

    [Fact]
    public void Mode1_PureResponses_ThrowsOnNullProjectClient()
        => Assert.Throws<ArgumentNullException>(() => new FoundryChatClient(aiProjectClient: null!, "gpt-4o-mini"));

    [Fact]
    public void Mode1_PureResponses_ThrowsOnEmptyModelId()
        => Assert.Throws<ArgumentException>(() => new FoundryChatClient(CreateProjectClient(), modelId: ""));

    #endregion

    #region Mode 2: server-side agent (direct unit tests)

    [Fact]
    public void Mode2_AgentReference_StampsFoundryProviderNameAndDefaultModelId()
    {
        // Arrange
        var projectClient = CreateProjectClient();
        var agentRef = new AgentReference("agent-name", "1");

        // Act
        var chatClient = new FoundryChatClient(projectClient, agentRef, defaultModelId: "gpt-4o", baseChatOptions: null);

        // Assert
        var metadata = chatClient.GetService<ChatClientMetadata>();
        Assert.NotNull(metadata);
        Assert.Equal("microsoft.foundry", metadata!.ProviderName);
        Assert.Equal("gpt-4o", metadata.DefaultModelId);
    }

    [Fact]
    public void Mode2_AgentReference_ExposesAgentReference_ViaGetService()
    {
        // Arrange
        var projectClient = CreateProjectClient();
        var agentRef = new AgentReference("agent-name", "1");

        // Act
        var chatClient = new FoundryChatClient(projectClient, agentRef, defaultModelId: null, baseChatOptions: null);

        // Assert
        Assert.Same(agentRef, chatClient.GetService<AgentReference>());
        Assert.Same(projectClient, chatClient.GetService<AIProjectClient>());
        Assert.NotNull(chatClient.GetService<ProjectOpenAIClient>());
        // Version/Record were not provided via this ctor.
        Assert.Null(chatClient.GetService<ProjectsAgentVersion>());
        Assert.Null(chatClient.GetService<ProjectsAgentRecord>());
    }

    [Fact]
    public void Mode2_AgentReference_AllowsNullDefaultModelIdAndBaseChatOptions()
    {
        // Arrange
        var projectClient = CreateProjectClient();
        var agentRef = new AgentReference("agent-name", "1");

        // Act + Assert: must not throw; defaultModelId and baseChatOptions are optional.
        var chatClient = new FoundryChatClient(projectClient, agentRef, defaultModelId: null, baseChatOptions: null);
        Assert.NotNull(chatClient);
    }

    [Fact]
    public void Mode2_AgentReference_ThrowsOnNullAgentReference()
        => Assert.Throws<ArgumentNullException>(() =>
            new FoundryChatClient(CreateProjectClient(), agentReference: null!, defaultModelId: null, baseChatOptions: null));

    #endregion

    #region Mode 2: end-to-end round-trip via AsAIAgent(AgentReference) extension

    // The end-to-end tests below exercise the same FoundryChatClient mode-2 behaviors above,
    // but through the public AsAIAgent(AgentReference) extension that constructs a FoundryChatClient
    // internally. They focus on the conversation-id handling that only manifests through the
    // ChatClientAgentSession surface, which requires a fully assembled agent rather than a bare
    // chat client.

    /// <summary>
    /// Verify that after the first RunAsync, the session's ConversationId is set from the
    /// response, and subsequent requests include that conversation ID automatically.
    /// </summary>
    [Fact]
    public async Task EndToEnd_AgentReference_UsesDefaultConversationIdAsync()
    {
        // Arrange
        var responsesRequestCount = 0;
        using var httpHandler = new HttpHandlerAssert(async (request) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.PathAndQuery.Contains("/responses"))
            {
                responsesRequestCount++;

                // Assert: On the second Responses API call, verify the conversation ID
                // from the first response is automatically included in the request body.
                if (responsesRequestCount == 2 && request.Content is not null)
                {
                    var requestBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Assert.Contains("resp_0888a", requestBody);
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TestDataUtil.GetOpenAIDefaultResponseJson(), Encoding.UTF8, "application/json") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TestDataUtil.GetAgentResponseJson(), Encoding.UTF8, "application/json") };
        });

#pragma warning disable CA5399
        using var httpClient = new HttpClient(httpHandler);
#pragma warning restore CA5399

        AIProjectClient projectClient = new(
            new Uri("https://test.openai.azure.com/"),
            new FakeAuthenticationTokenProvider(),
            new AIProjectClientOptions() { Transport = new HttpClientPipelineTransport(httpClient) });

        var agent = projectClient.AsAIAgent(new AgentReference("agent-name"));

        // Act
        var session = await agent.CreateSessionAsync();
        await agent.RunAsync("Hello", session);
        await agent.RunAsync("Follow up", session);

        // Assert
        Assert.Equal(2, responsesRequestCount);
        var chatClientSession = Assert.IsType<ChatClientAgentSession>(session);
        Assert.Equal("resp_0888a46cbf2b1ff3006914596e05d08195a77c3f5187b769a7", chatClientSession.ConversationId);
    }

    /// <summary>
    /// Verify that when the chat client doesn't have a default "conv_" conversation id, the chat client still uses the conversation ID in HTTP requests.
    /// </summary>
    [Fact]
    public async Task EndToEnd_AgentReference_UsesPerRequestConversationId_WhenNoDefaultConversationIdIsProvidedAsync()
    {
        // Arrange
        var requestTriggered = false;
        using var httpHandler = new HttpHandlerAssert(async (request) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.PathAndQuery.Contains("/responses"))
            {
                requestTriggered = true;

                // Assert
                if (request.Content is not null)
                {
                    var requestBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Assert.Contains("conv_12345", requestBody);
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TestDataUtil.GetOpenAIDefaultResponseJson(), Encoding.UTF8, "application/json") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TestDataUtil.GetAgentResponseJson(), Encoding.UTF8, "application/json") };
        });

#pragma warning disable CA5399
        using var httpClient = new HttpClient(httpHandler);
#pragma warning restore CA5399

        AIProjectClient projectClient = new(
            new Uri("https://test.openai.azure.com/"),
            new FakeAuthenticationTokenProvider(),
            new AIProjectClientOptions() { Transport = new HttpClientPipelineTransport(httpClient) });

        var agent = projectClient.AsAIAgent(new AgentReference("agent-name"));

        // Act
        var session = await agent.CreateSessionAsync();
        await agent.RunAsync("Hello", session, options: new ChatClientAgentRunOptions() { ChatOptions = new() { ConversationId = "conv_12345" } });

        Assert.True(requestTriggered);
        var chatClientSession = Assert.IsType<ChatClientAgentSession>(session);
        Assert.Equal("conv_12345", chatClientSession.ConversationId);
    }

    /// <summary>
    /// Verify that even when the chat client has a default conversation id, the chat client will prioritize the per-request conversation id provided in HTTP requests.
    /// </summary>
    [Fact]
    public async Task EndToEnd_AgentReference_UsesPerRequestConversationId_EvenWhenDefaultConversationIdIsProvidedAsync()
    {
        // Arrange
        var requestTriggered = false;
        using var httpHandler = new HttpHandlerAssert(async (request) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.PathAndQuery.Contains("/responses"))
            {
                requestTriggered = true;

                // Assert
                if (request.Content is not null)
                {
                    var requestBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Assert.Contains("conv_12345", requestBody);
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TestDataUtil.GetOpenAIDefaultResponseJson(), Encoding.UTF8, "application/json") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TestDataUtil.GetAgentResponseJson(), Encoding.UTF8, "application/json") };
        });

#pragma warning disable CA5399
        using var httpClient = new HttpClient(httpHandler);
#pragma warning restore CA5399

        AIProjectClient projectClient = new(
            new Uri("https://test.openai.azure.com/"),
            new FakeAuthenticationTokenProvider(),
            new AIProjectClientOptions() { Transport = new HttpClientPipelineTransport(httpClient) });

        var agent = projectClient.AsAIAgent(new AgentReference("agent-name"));

        // Act
        var session = await agent.CreateSessionAsync();
        await agent.RunAsync("Hello", session, options: new ChatClientAgentRunOptions() { ChatOptions = new() { ConversationId = "conv_12345" } });

        Assert.True(requestTriggered);
        var chatClientSession = Assert.IsType<ChatClientAgentSession>(session);
        Assert.Equal("conv_12345", chatClientSession.ConversationId);
    }

    /// <summary>
    /// Verify that when the chat client is provided without a "conv_" prefixed conversation ID, the chat client uses the previous conversation ID in HTTP requests.
    /// </summary>
    [Fact]
    public async Task EndToEnd_AgentReference_UsesPreviousResponseId_WhenConversationIsNotPrefixedAsConvAsync()
    {
        // Arrange
        var requestTriggered = false;
        using var httpHandler = new HttpHandlerAssert(async (request) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.PathAndQuery.Contains("/responses"))
            {
                requestTriggered = true;

                // Assert
                if (request.Content is not null)
                {
                    var requestBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Assert.Contains("resp_0888a", requestBody);
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TestDataUtil.GetOpenAIDefaultResponseJson(), Encoding.UTF8, "application/json") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TestDataUtil.GetAgentResponseJson(), Encoding.UTF8, "application/json") };
        });

#pragma warning disable CA5399
        using var httpClient = new HttpClient(httpHandler);
#pragma warning restore CA5399

        AIProjectClient projectClient = new(
            new Uri("https://test.openai.azure.com/"),
            new FakeAuthenticationTokenProvider(),
            new AIProjectClientOptions() { Transport = new HttpClientPipelineTransport(httpClient) });

        var agent = projectClient.AsAIAgent(new AgentReference("agent-name"));

        // Act
        var session = await agent.CreateSessionAsync();
        await agent.RunAsync("Hello", session, options: new ChatClientAgentRunOptions() { ChatOptions = new() { ConversationId = "resp_0888a" } });

        Assert.True(requestTriggered);
        var chatClientSession = Assert.IsType<ChatClientAgentSession>(session);
        Assert.Equal("resp_0888a46cbf2b1ff3006914596e05d08195a77c3f5187b769a7", chatClientSession.ConversationId);
    }

    #endregion

    #region Mode 3: hosted agent endpoint

    [Fact]
    public void Mode3_HostedAgentEndpoint_ParsesAgentNameFromUrl()
    {
        // Arrange + Act
        var chatClient = new FoundryChatClient(
            agentEndpoint: new Uri("https://example.com/api/projects/myproj/agents/myagent/endpoint/protocols/openai"),
            credential: new FakeAuthenticationTokenProvider(),
            clientOptions: null);

        // Assert
        Assert.Equal("myagent", chatClient.HostedAgentName);
    }

    [Fact]
    public void Mode3_HostedAgentEndpoint_StampsFoundryProviderName()
    {
        // Act
        var chatClient = new FoundryChatClient(
            agentEndpoint: new Uri("https://example.com/api/projects/myproj/agents/myagent/endpoint/protocols/openai"),
            credential: new FakeAuthenticationTokenProvider(),
            clientOptions: null);

        // Assert
        var metadata = chatClient.GetService<ChatClientMetadata>();
        Assert.NotNull(metadata);
        Assert.Equal("microsoft.foundry", metadata!.ProviderName);
        // No model id is knowable from the URL alone.
        Assert.Null(metadata.DefaultModelId);
    }

    [Fact]
    public void Mode3_HostedAgentEndpoint_ExposesProjectOpenAIClient_ButNotAIProjectClient()
    {
        // Act
        var chatClient = new FoundryChatClient(
            agentEndpoint: new Uri("https://example.com/api/projects/myproj/agents/myagent/endpoint/protocols/openai"),
            credential: new FakeAuthenticationTokenProvider(),
            clientOptions: null);

        // Assert
        Assert.NotNull(chatClient.GetService<ProjectOpenAIClient>());
        // Mode 3 builds its own ProjectOpenAIClient directly; no AIProjectClient is involved.
        Assert.Null(chatClient.GetService<AIProjectClient>());
        Assert.Null(chatClient.GetService<AgentReference>());
        Assert.Null(chatClient.GetService<ProjectsAgentVersion>());
        Assert.Null(chatClient.GetService<ProjectsAgentRecord>());
    }

    [Fact]
    public void Mode3_HostedAgentEndpoint_ThrowsOnNullEndpoint()
        => Assert.Throws<ArgumentNullException>(() =>
            new FoundryChatClient(agentEndpoint: null!, credential: new FakeAuthenticationTokenProvider(), clientOptions: null));

    [Fact]
    public void Mode3_HostedAgentEndpoint_ThrowsOnNullCredential()
        => Assert.Throws<ArgumentNullException>(() =>
            new FoundryChatClient(
                agentEndpoint: new Uri("https://example.com/api/projects/myproj/agents/myagent/endpoint/protocols/openai"),
                credential: null!,
                clientOptions: null));

    #endregion

    #region ParseAgentEndpoint URL parsing

    [Fact]
    public void ParseAgentEndpoint_HappyPath_ReturnsAgentNameAndProjectRoot()
    {
        // Act
        var (agentName, projectRoot) = FoundryChatClient.ParseAgentEndpoint(
            new Uri("https://example.com/api/projects/myproj/agents/myagent/endpoint/protocols/openai"));

        // Assert
        Assert.Equal("myagent", agentName);
        Assert.Equal("https://example.com/api/projects/myproj", projectRoot.AbsoluteUri.TrimEnd('/'));
    }

    [Fact]
    public void ParseAgentEndpoint_TolerantOfTrailingSlash()
    {
        // Act
        var (agentName, _) = FoundryChatClient.ParseAgentEndpoint(
            new Uri("https://example.com/api/projects/myproj/agents/myagent/endpoint/protocols/openai/"));

        // Assert
        Assert.Equal("myagent", agentName);
    }

    [Fact]
    public void ParseAgentEndpoint_TolerantOfCaseDifferencesOnAgentsSegment()
    {
        // Act
        var (agentName, _) = FoundryChatClient.ParseAgentEndpoint(
            new Uri("https://example.com/api/projects/myproj/AGENTS/myagent/endpoint/protocols/openai"));

        // Assert
        Assert.Equal("myagent", agentName);
    }

    [Fact]
    public void ParseAgentEndpoint_StripsQueryAndFragment()
    {
        // Act
        var (_, projectRoot) = FoundryChatClient.ParseAgentEndpoint(
            new Uri("https://example.com/api/projects/myproj/agents/myagent/endpoint/protocols/openai?api-version=v1#frag"));

        // Assert
        Assert.Equal(string.Empty, projectRoot.Query);
        Assert.Equal(string.Empty, projectRoot.Fragment);
    }

    [Fact]
    public void ParseAgentEndpoint_ThrowsOnMissingAgentsSegment()
        => Assert.Throws<ArgumentException>(() =>
            FoundryChatClient.ParseAgentEndpoint(new Uri("https://example.com/api/projects/myproj/anyseg/myagent/endpoint/protocols/openai")));

    [Fact]
    public void ParseAgentEndpoint_ThrowsOnWrongSuffix()
        => Assert.Throws<ArgumentException>(() =>
            FoundryChatClient.ParseAgentEndpoint(new Uri("https://example.com/api/projects/myproj/agents/myagent/wrong/suffix")));

    [Fact]
    public void ParseAgentEndpoint_ThrowsOnNullUri()
        => Assert.Throws<ArgumentNullException>(() => FoundryChatClient.ParseAgentEndpoint(null!));

    #endregion

    #region AgentFrameworkUserAgentPolicy registration + dedup

    [Fact]
    public void Register_AgentFrameworkUserAgentPolicy_OnUnderlyingOpenAIRequestPolicies()
    {
        // Arrange + Act: constructing a FoundryChatClient should register the
        // AgentFrameworkUserAgentPolicy on the inner chat client's OpenAIRequestPolicies.
        var chatClient = new FoundryChatClient(CreateProjectClient(), "gpt-4o-mini");

        // Assert: the inner chat client (MEAI's OpenAIResponsesChatClient) exposes
        // OpenAIRequestPolicies via GetService, and our policy is present in its entries.
        var policies = chatClient.GetService<OpenAIRequestPolicies>();
        Assert.NotNull(policies);
        Assert.Equal(1, EntriesCount(policies!));
    }

    [Fact]
    public void Register_AgentFrameworkUserAgentPolicy_IsDedupedAcrossMultipleClients_OnSharedInner()
    {
        // Arrange: construct via the ProjectsAgentVersion mode-2 variant, which chains via
        // :this(...) into the AgentReference ctor. If the policy registration code were
        // inadvertently called twice along the chain, we would see 2 entries.
        var projectClient = CreateProjectClient();
        var agentVersion = ModelReaderWriter.Read<ProjectsAgentVersion>(
            BinaryData.FromString(TestDataUtil.GetAgentVersionResponseJson()))!;

        // Act
        var chatClient = new FoundryChatClient(projectClient, agentVersion, baseChatOptions: null);

        // Assert: even though the version variant funnels through the AgentReference ctor
        // via :this(...), the policy is registered exactly once on the inner pipeline.
        var policies = chatClient.GetService<OpenAIRequestPolicies>();
        Assert.NotNull(policies);
        Assert.Equal(1, EntriesCount(policies!));
        Assert.Same(agentVersion, chatClient.GetService<ProjectsAgentVersion>());
        Assert.NotNull(chatClient.GetService<AgentReference>());
    }

    #endregion

    #region Helpers

    private static AIProjectClient CreateProjectClient()
        => new(
            new Uri("https://test.openai.azure.com/"),
            new FakeAuthenticationTokenProvider(),
            new AIProjectClientOptions { Transport = new HttpClientPipelineTransport(new HttpClient()) });

    private static int EntriesCount(OpenAIRequestPolicies policies)
    {
        var field = typeof(OpenAIRequestPolicies).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var arr = (Array)field!.GetValue(policies)!;
        return arr.Length;
    }

    #endregion
}
#pragma warning restore CS0618
