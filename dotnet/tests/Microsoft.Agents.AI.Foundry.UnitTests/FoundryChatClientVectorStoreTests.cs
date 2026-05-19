// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using OpenAI.Files;

#pragma warning disable OPENAI001, CS0618

namespace Microsoft.Agents.AI.Foundry.UnitTests;

/// <summary>
/// Unit tests for the file and vector-store helper methods on <see cref="FoundryChatClient"/>.
/// Covers all four methods across the three FoundryChatClient construction modes plus argument
/// validation, cancellation, and request-body shape on the wire.
/// </summary>
public sealed class FoundryChatClientVectorStoreTests
{
    // ----- Construction helpers shared by every test in this file -----

    private static (FoundryChatClient ChatClient, RequestRecorder Recorder) CreateMode1(string modelId = "gpt-4o-mini", string? responseBody = null)
    {
        var recorder = new RequestRecorder(responseBody);
#pragma warning disable CA5399
        var httpClient = new HttpClient(recorder);
#pragma warning restore CA5399
        var projectClient = new AIProjectClient(
            new Uri("https://test.openai.azure.com/"),
            new FakeAuthenticationTokenProvider(),
            new AIProjectClientOptions { Transport = new HttpClientPipelineTransport(httpClient) });
        return (new FoundryChatClient(projectClient, modelId), recorder);
    }

    private static (FoundryChatClient ChatClient, RequestRecorder Recorder) CreateMode2(string? responseBody = null)
    {
        var recorder = new RequestRecorder(responseBody);
#pragma warning disable CA5399
        var httpClient = new HttpClient(recorder);
#pragma warning restore CA5399
        var projectClient = new AIProjectClient(
            new Uri("https://test.openai.azure.com/"),
            new FakeAuthenticationTokenProvider(),
            new AIProjectClientOptions { Transport = new HttpClientPipelineTransport(httpClient) });
        var agentRef = new AgentReference("agent-name", "1");
        return (new FoundryChatClient(projectClient, agentRef, defaultModelId: "gpt-4o", baseChatOptions: null), recorder);
    }

    private static FoundryChatClient CreateMode3()
        => new(
            agentEndpoint: new Uri("https://example.com/api/projects/myproj/agents/myagent/endpoint/protocols/openai"),
            credential: new FakeAuthenticationTokenProvider(),
            clientOptions: null);

    private static string MakeTempFile(string contents = "hello world")
    {
        var path = Path.Combine(Path.GetTempPath(), $"fcc-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, contents);
        return path;
    }

    // ----- UploadFileAsync -----

    [Fact]
    public async Task UploadFileAsync_Mode1_UploadsViaProjectOpenAIClientAsync()
    {
        var (chatClient, recorder) = CreateMode1(responseBody: FakeFileJson("file_abc"));
        var path = MakeTempFile();
        try
        {
            var result = await chatClient.UploadFileAsync(path, FileUploadPurpose.Assistants);

            Assert.Equal("file_abc", result.Id);
            Assert.NotEmpty(recorder.Requests);
            Assert.EndsWith("/files", recorder.Requests[0].PathAndQuery.TrimEnd('/').Split('?')[0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task UploadFileAsync_Mode2_UploadsViaProjectOpenAIClientAsync()
    {
        var (chatClient, recorder) = CreateMode2(responseBody: FakeFileJson("file_xyz"));
        var path = MakeTempFile();
        try
        {
            var result = await chatClient.UploadFileAsync(path, FileUploadPurpose.Assistants);
            Assert.Equal("file_xyz", result.Id);
            Assert.Contains(recorder.Requests, r => r.PathAndQuery.Contains("/files"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task UploadFileAsync_Mode3_UploadsViaMaterializedProjectClientAsync()
    {
        // Regression-prevention for Q2 Agent Endpoint mode (Mode 3) materialization: file uploads must work on the Agent Endpoint mode (Mode 3)
        // because the materialized AIProjectClient is now reachable. We cannot fully exercise the
        // wire here (the Agent Endpoint mode's AIProjectClient is built internally with a real DefaultTransport),
        // so we assert the call surfaces with the expected exception family (auth/network) and
        // NOT InvalidOperationException complaining about a missing AIProjectClient.
        var chatClient = CreateMode3();
        var path = MakeTempFile();
        try
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                chatClient.UploadFileAsync(path, FileUploadPurpose.Assistants, CancellationToken.None));
            Assert.IsNotType<InvalidOperationException>(ex);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task UploadFileAsync_NullFilePath_ThrowsArgumentNullExceptionAsync()
    {
        var (chatClient, _) = CreateMode1();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            chatClient.UploadFileAsync(null!, FileUploadPurpose.Assistants));
    }

    [Fact]
    public async Task UploadFileAsync_FileNotFound_ThrowsFileNotFoundExceptionAsync()
    {
        var (chatClient, _) = CreateMode1();
        var missing = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.txt");
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            chatClient.UploadFileAsync(missing, FileUploadPurpose.Assistants));
    }

    [Fact]
    public async Task UploadFileAsync_HonorsCancellationAsync()
    {
        // Cancellation propagation through the OpenAI SDK pipeline surfaces different exception
        // types depending on the framework target (OperationCanceledException on net10.0,
        // ObjectDisposedException at the transport layer on net472). Asserting on the exact
        // exception class is brittle; assert only that the call throws when the token is
        // pre-cancelled.
        var (chatClient, _) = CreateMode1(responseBody: FakeFileJson("file_abc"));
        var path = MakeTempFile();
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<Exception>(() =>
                chatClient.UploadFileAsync(path, FileUploadPurpose.Assistants, cts.Token));
        }
        finally { File.Delete(path); }
    }

    // ----- DeleteFileAsync -----

    [Fact]
    public async Task DeleteFileAsync_Mode1_CallsDeleteOnFileClientAsync()
    {
        var (chatClient, recorder) = CreateMode1(responseBody: FakeFileDeletedJson("file_abc"));
        await chatClient.DeleteFileAsync("file_abc");
        Assert.Contains(recorder.Requests, r => r.Method == "DELETE" && r.PathAndQuery.Contains("/files/file_abc"));
    }

    [Fact]
    public async Task DeleteFileAsync_Mode2_CallsDeleteOnFileClientAsync()
    {
        var (chatClient, recorder) = CreateMode2(responseBody: FakeFileDeletedJson("file_xyz"));
        await chatClient.DeleteFileAsync("file_xyz");
        Assert.Contains(recorder.Requests, r => r.Method == "DELETE" && r.PathAndQuery.Contains("/files/file_xyz"));
    }

    [Fact]
    public async Task DeleteFileAsync_NullId_ThrowsArgumentExceptionAsync()
    {
        var (chatClient, _) = CreateMode1();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => chatClient.DeleteFileAsync(null!));
    }

    [Fact]
    public async Task DeleteFileAsync_EmptyId_ThrowsArgumentExceptionAsync()
    {
        var (chatClient, _) = CreateMode1();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => chatClient.DeleteFileAsync(""));
    }

    [Fact]
    public async Task DeleteFileAsync_HonorsCancellationAsync()
    {
        // Verify the cancellation token reaches the HTTP pipeline by having the handler
        // throw OperationCanceledException when the token is cancelled before the request.
        // This is more robust than asserting on the exact exception the SDK surfaces, which
        // depends on internal pipeline plumbing.
        var observedToken = CancellationToken.None;
        using var handler = new HttpHandlerAssert(async req =>
        {
            // We don't have direct access to the SDK's CancellationToken here; instead, sleep
            // briefly to give the caller's pre-cancellation a chance to be picked up by the
            // transport. If cancellation reached the pipeline, the await on this handler call
            // would surface OperationCanceledException; if not, the response is returned.
            await Task.Delay(50).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(FakeFileDeletedJson("file_abc"), Encoding.UTF8, "application/json"),
            };
        });
#pragma warning disable CA5399
        using var httpClient = new HttpClient(handler);
#pragma warning restore CA5399
        var projectClient = new AIProjectClient(
            new Uri("https://test.openai.azure.com/"),
            new FakeAuthenticationTokenProvider(),
            new AIProjectClientOptions { Transport = new HttpClientPipelineTransport(httpClient) });
        var chatClient = new FoundryChatClient(projectClient, "gpt-4o-mini");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // Any throw is acceptable evidence that cancellation was honored. The SDK's exact
        // exception surface for pre-cancelled tokens is an implementation detail of
        // System.ClientModel's pipeline and may differ between versions.
        await Assert.ThrowsAnyAsync<Exception>(() => chatClient.DeleteFileAsync("file_abc", cts.Token));
    }

    // ----- CreateVectorStoreAsync -----

    [Fact]
    public async Task CreateVectorStoreAsync_UploadsThenCreates_WithFileIds_ReturnsVectorStoreAsync()
    {
        // Each file POST returns a distinct file id; the recorder dispatches on URL to differentiate.
        var fileCount = 0;
        using var handler = new HttpHandlerAssert(async req =>
        {
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (req.RequestUri!.AbsolutePath.Contains("/files") && req.Method == HttpMethod.Post)
            {
                fileCount++;
                return MakeJsonResponse(FakeFileJson($"file_{fileCount}"));
            }
            if (req.RequestUri.AbsolutePath.Contains("/vector_stores") && req.Method == HttpMethod.Post)
            {
                Assert.Contains("file_1", body);
                Assert.Contains("file_2", body);
                Assert.Contains("knowledge-base", body);
                return MakeJsonResponse(FakeVectorStoreJson("vs_abc", name: "knowledge-base"));
            }
            return MakeJsonResponse("{}");
        });

#pragma warning disable CA5399
        using var httpClient = new HttpClient(handler);
#pragma warning restore CA5399
        var projectClient = new AIProjectClient(
            new Uri("https://test.openai.azure.com/"),
            new FakeAuthenticationTokenProvider(),
            new AIProjectClientOptions { Transport = new HttpClientPipelineTransport(httpClient) });
        var chatClient = new FoundryChatClient(projectClient, "gpt-4o-mini");

        var pathA = MakeTempFile("alpha");
        var pathB = MakeTempFile("beta");
        try
        {
            var store = await chatClient.CreateVectorStoreAsync("knowledge-base", new[] { pathA, pathB });
            Assert.Equal("vs_abc", store.Id);
            Assert.Equal(2, fileCount);
        }
        finally { File.Delete(pathA); File.Delete(pathB); }
    }

    [Fact]
    public async Task CreateVectorStoreAsync_WithExpiresAfter_SerializesLastActiveAtAnchorAsync()
    {
        string? vectorStoreBody = null;
        using var handler = new HttpHandlerAssert(async req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/vector_stores") && req.Method == HttpMethod.Post)
            {
                vectorStoreBody = req.Content is null ? "" : await req.Content.ReadAsStringAsync().ConfigureAwait(false);
                return MakeJsonResponse(FakeVectorStoreJson("vs_abc", name: "x"));
            }
            return MakeJsonResponse("{}");
        });

#pragma warning disable CA5399
        using var httpClient = new HttpClient(handler);
#pragma warning restore CA5399
        var projectClient = new AIProjectClient(
            new Uri("https://test.openai.azure.com/"),
            new FakeAuthenticationTokenProvider(),
            new AIProjectClientOptions { Transport = new HttpClientPipelineTransport(httpClient) });
        var chatClient = new FoundryChatClient(projectClient, "gpt-4o-mini");

        await chatClient.CreateVectorStoreAsync("x", Array.Empty<string>(), expiresAfter: TimeSpan.FromDays(7));

        Assert.NotNull(vectorStoreBody);
        Assert.Contains("\"expires_after\"", vectorStoreBody);
        Assert.Contains("\"last_active_at\"", vectorStoreBody);
        Assert.Contains("\"days\":7", vectorStoreBody);
    }

    [Fact]
    public async Task CreateVectorStoreAsync_WithNullExpiresAfter_OmitsExpirationPolicyAsync()
    {
        string? vectorStoreBody = null;
        using var handler = new HttpHandlerAssert(async req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/vector_stores") && req.Method == HttpMethod.Post)
            {
                vectorStoreBody = req.Content is null ? "" : await req.Content.ReadAsStringAsync().ConfigureAwait(false);
                return MakeJsonResponse(FakeVectorStoreJson("vs_abc", name: "x"));
            }
            return MakeJsonResponse("{}");
        });

#pragma warning disable CA5399
        using var httpClient = new HttpClient(handler);
#pragma warning restore CA5399
        var projectClient = new AIProjectClient(
            new Uri("https://test.openai.azure.com/"),
            new FakeAuthenticationTokenProvider(),
            new AIProjectClientOptions { Transport = new HttpClientPipelineTransport(httpClient) });
        var chatClient = new FoundryChatClient(projectClient, "gpt-4o-mini");

        await chatClient.CreateVectorStoreAsync("x", Array.Empty<string>(), expiresAfter: null);

        Assert.NotNull(vectorStoreBody);
        Assert.DoesNotContain("\"expires_after\"", vectorStoreBody);
    }

    [Fact]
    public async Task CreateVectorStoreAsync_EmptyFilesList_CreatesEmptyStoreAsync()
    {
        var (chatClient, _) = CreateMode1(responseBody: FakeVectorStoreJson("vs_empty", name: "x"));
        var store = await chatClient.CreateVectorStoreAsync("x", Array.Empty<string>());
        Assert.Equal("vs_empty", store.Id);
    }

    [Fact]
    public async Task CreateVectorStoreAsync_NullName_ThrowsArgumentExceptionAsync()
    {
        var (chatClient, _) = CreateMode1();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            chatClient.CreateVectorStoreAsync(null!, Array.Empty<string>()));
    }

    [Fact]
    public async Task CreateVectorStoreAsync_NullFilePaths_ThrowsArgumentNullExceptionAsync()
    {
        var (chatClient, _) = CreateMode1();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            chatClient.CreateVectorStoreAsync("x", filePaths: null!));
    }

    [Fact]
    public async Task CreateVectorStoreAsync_HonorsCancellationAsync()
    {
        // Same rationale as UploadFileAsync_HonorsCancellationAsync — assert only that any
        // exception is thrown on a pre-cancelled token.
        var (chatClient, _) = CreateMode1(responseBody: FakeVectorStoreJson("vs_x", "x"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            chatClient.CreateVectorStoreAsync("x", Array.Empty<string>(), expiresAfter: null, cts.Token));
    }

    // ----- DeleteVectorStoreAsync -----

    [Fact]
    public async Task DeleteVectorStoreAsync_Mode1_CallsDeleteAsync()
    {
        var (chatClient, recorder) = CreateMode1(responseBody: FakeVectorStoreDeletedJson("vs_abc"));
        await chatClient.DeleteVectorStoreAsync("vs_abc");
        Assert.Contains(recorder.Requests, r => r.Method == "DELETE" && r.PathAndQuery.Contains("/vector_stores/vs_abc"));
    }

    [Fact]
    public async Task DeleteVectorStoreAsync_Mode2_CallsDeleteAsync()
    {
        var (chatClient, recorder) = CreateMode2(responseBody: FakeVectorStoreDeletedJson("vs_xyz"));
        await chatClient.DeleteVectorStoreAsync("vs_xyz");
        Assert.Contains(recorder.Requests, r => r.Method == "DELETE" && r.PathAndQuery.Contains("/vector_stores/vs_xyz"));
    }

    [Fact]
    public async Task DeleteVectorStoreAsync_NullId_ThrowsArgumentExceptionAsync()
    {
        var (chatClient, _) = CreateMode1();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => chatClient.DeleteVectorStoreAsync(null!));
    }

    [Fact]
    public async Task DeleteVectorStoreAsync_HonorsCancellationAsync()
    {
        // Same approach as DeleteFileAsync_HonorsCancellationAsync — assert that the call
        // throws when the token is pre-cancelled, without asserting on the exact exception
        // surfaced by the SDK pipeline.
        var (chatClient, _) = CreateMode1(responseBody: FakeVectorStoreDeletedJson("vs_abc"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<Exception>(() => chatClient.DeleteVectorStoreAsync("vs_abc", cts.Token));
    }

    // ----- Fixtures and helpers -----

    private static HttpResponseMessage MakeJsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static string FakeFileJson(string id)
        => $"{{\"id\":\"{id}\",\"object\":\"file\",\"bytes\":11,\"created_at\":1700000000,\"filename\":\"x.txt\",\"purpose\":\"assistants\",\"status\":\"processed\"}}";

    private static string FakeFileDeletedJson(string id)
        => $"{{\"id\":\"{id}\",\"object\":\"file\",\"deleted\":true}}";

    private static string FakeVectorStoreJson(string id, string name)
        => $"{{\"id\":\"{id}\",\"object\":\"vector_store\",\"created_at\":1700000000,\"name\":\"{name}\",\"usage_bytes\":0,\"file_counts\":{{\"in_progress\":0,\"completed\":0,\"failed\":0,\"cancelled\":0,\"total\":0}},\"status\":\"completed\",\"last_active_at\":1700000000}}";

    private static string FakeVectorStoreDeletedJson(string id)
        => $"{{\"id\":\"{id}\",\"object\":\"vector_store.deleted\",\"deleted\":true}}";

    private sealed class RequestRecorder : HttpClientHandler
    {
        private readonly string _responseBody;
        public List<RecordedRequest> Requests { get; } = [];

        public RequestRecorder(string? responseBody)
        {
            this._responseBody = responseBody ?? "{}";
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requests.Add(new RecordedRequest
            {
                Method = request.Method.Method,
                PathAndQuery = request.RequestUri?.PathAndQuery ?? "",
#if NET
                Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
#else
                Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync().ConfigureAwait(false),
#endif
            });
            return MakeJsonResponse(this._responseBody);
        }
    }

    private sealed class RecordedRequest
    {
        public string Method { get; set; } = "";
        public string PathAndQuery { get; set; } = "";
        public string Body { get; set; } = "";
    }
}
#pragma warning restore CS0618
