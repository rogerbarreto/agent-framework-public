// Copyright (c) Microsoft. All rights reserved.

using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Azure.AI.Projects;
using Hosted_Shared_Contributor_Setup;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

internal sealed class ResilienceE2EHostedServerManager(
    VerificationOptions options,
    ResilienceE2EHostedServerManager.InterruptionKind interruption,
    int pauseMilliseconds) : IAsyncDisposable
{
    private const string AgentName = "hosted-workflow-resilient-long-running";
    private readonly string _repositoryRoot = FindRepositoryRoot();
    private readonly string _workingRoot = Path.Combine(
        Path.GetTempPath(),
        $"maf-resilience-{interruption}-{Guid.NewGuid():N}");
    private readonly int _port = GetAvailablePort();
    private readonly StreamWriter _logWriter = new(
        Path.Combine(
            Path.GetTempPath(),
            $"maf-resilience-{interruption}-{Guid.NewGuid():N}.log"),
        append: false,
        new UTF8Encoding(false))
    {
        AutoFlush = true,
    };
    private ServerProcess? _server;
    private LocalAgentClient? _agentClient;
    private bool _succeeded;

    public VerificationOptions Options { get; } = options;

    public string StateRoot => Path.Combine(this._workingRoot, "state");

    public string LogPath => ((FileStream)this._logWriter.BaseStream).Name;

    public Uri BaseAddress => new($"http://127.0.0.1:{this._port}");

    public HttpClient ControlClient { get; } = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <summary>
    /// Gets the <see cref="AIAgent"/> instance pointing to the hosted server.
    /// </summary>
    /// <returns>The <see cref="AIAgent"/> instance.</returns>
    public AIAgent GetAIAgent() => (this._agentClient ??= CreateClientAgent(this.BaseAddress, AgentName)).Agent;

    public async Task BuildServerAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(this._workingRoot);
        string serverProject = Path.Combine(
            this._repositoryRoot,
            "dotnet",
            "samples",
            "04-hosting",
            "FoundryHostedAgents",
            "responses",
            "Hosted-Workflow-Resilient-Long-Running",
            "HostedWorkflowResilientLongRunning.csproj");
        string serverOutput = Path.Combine(this._workingRoot, "server");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(serverProject)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(serverProject);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Debug");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(serverOutput);
        startInfo.ArgumentList.Add("--tl:off");
        startInfo.Environment["DOTNET_NOLOGO"] = "true";

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the server build.");
        TextWriter synchronizedLogWriter = TextWriter.Synchronized(this._logWriter);
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                synchronizedLogWriter.WriteLine($"[build stdout] {eventArgs.Data}");
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                synchronizedLogWriter.WriteLine($"[build stderr] {eventArgs.Data}");
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Server build failed with exit code {process.ExitCode}.");
        }
    }

    public async Task<int> StartServerAsync(CancellationToken cancellationToken)
    {
        if (this._server is not null)
        {
            throw new InvalidOperationException("The server is already running.");
        }

        string serverAssembly = Path.Combine(
            this._workingRoot,
            "server",
            "HostedWorkflowResilientLongRunning.dll");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(serverAssembly)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(serverAssembly);
        startInfo.Environment["AGENTSERVER_STATE_ROOT"] = this.StateRoot;
        startInfo.Environment["FOUNDRY_AGENT_SESSION_ID"] = $"using-e2e-resilience-{interruption}";
        startInfo.Environment["AGENT_NAME"] = AgentName;
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{this._port}";
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ENABLE_E2E_SHUTDOWN_ENDPOINT"] =
            string.Equals(
                interruption.ToString(),
                InterruptionKind.Shutdown.ToString(),
                StringComparison.OrdinalIgnoreCase)
                ? "true"
                : "false";
        startInfo.Environment["DOTNET_NOLOGO"] = "true";
        startInfo.Environment.Remove("FOUNDRY_HOSTING_ENVIRONMENT");

        this._server = ServerProcess.Start(startInfo, this._logWriter);
        await WaitForReadinessAsync(
            this.ControlClient,
            this.BaseAddress,
            cancellationToken);
        return this._server.Id;
    }

    public async Task CrashServerAsync()
    {
        ServerProcess server = this._server
            ?? throw new InvalidOperationException("The server is not running.");
        await server.KillAsync();
        this._server = null;
    }

    public async Task RequestShutdownAsync(
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await this.ControlClient.PostAsync(
            new Uri(this.BaseAddress, "shutdown"),
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task WaitForServerExitAsync(
        CancellationToken cancellationToken)
    {
        ServerProcess server = this._server
            ?? throw new InvalidOperationException("The server is not running.");
        await server.WaitForExitAsync(cancellationToken);
        this._server = null;
    }

    public void DeleteStaleStreamLocks()
    {
        string streamsPath = Path.Combine(this.StateRoot, "streams");
        if (!Directory.Exists(streamsPath))
        {
            return;
        }

        foreach (string lockPath in Directory.EnumerateFiles(
            streamsPath,
            "*.jsonl.lock",
            SearchOption.TopDirectoryOnly))
        {
            for (int attempt = 1; attempt <= 10; attempt++)
            {
                try
                {
                    File.Delete(lockPath);
                    break;
                }
                catch (UnauthorizedAccessException) when (attempt < 10)
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(250));
                }
                catch (IOException) when (attempt < 10)
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(250));
                }
            }
        }
    }

    public async Task<bool> LogContainsAsync(
        string text,
        CancellationToken cancellationToken)
    {
        await this._logWriter.FlushAsync(cancellationToken);
        await using FileStream stream = new(
            this.LogPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        string log = await reader.ReadToEndAsync(cancellationToken);
        return log.Contains(text, StringComparison.Ordinal);
    }

    public int InterruptValue =>
        this.Options.Target - this.Options.InterruptAfterCount + 1;

    public void MarkSucceeded() => this._succeeded = true;

    public async ValueTask DisposeAsync()
    {
        string logPath = this.LogPath;
        if (this._server is not null)
        {
            await this._server.KillAsync();
        }

        this.ControlClient.Dispose();
        this._agentClient?.Dispose();
        await this._logWriter.DisposeAsync();

        if (this._succeeded)
        {
            TryDeleteDirectory(this._workingRoot);
            TryDeleteFile(logPath);
        }
        else
        {
            Console.Error.WriteLine(
                $"E2E working directory retained at: {this._workingRoot}");
            Console.Error.WriteLine($"Server log: {logPath}");
        }
    }

    private static LocalAgentClient CreateClientAgent(
        Uri baseAddress,
        string agentName)
    {
        Uri httpsProjectEndpoint = new UriBuilder(baseAddress)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = baseAddress.Port,
        }.Uri;

        var transportClient = new HttpClient(
            new LocalHttpSchemeRewriteHandler(baseAddress));
        var clientOptions = new AIProjectClientOptions
        {
            Transport = new HttpClientPipelineTransport(transportClient),
        };

        AIAgent agent = new AIProjectClient(
            httpsProjectEndpoint,
            new LocalDevelopmentTokenCredential(),
            clientOptions)
            .AsAIAgent(
                model: agentName,
                instructions: "Invoke the local hosted countdown workflow.");
        return new LocalAgentClient(agent, transportClient);
    }

    private static async Task WaitForReadinessAsync(
        HttpClient client,
        Uri baseAddress,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var requestCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                requestCancellation.CancelAfter(TimeSpan.FromSeconds(2));
                using HttpResponseMessage response = await client.GetAsync(
                    new Uri(baseAddress, "readiness"),
                    requestCancellation.Token);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (Exception exception)
                when (exception is HttpRequestException
                    or TaskCanceledException)
            {
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(250),
                cancellationToken);
        }

        throw new TimeoutException(
            "Server did not become ready within 30 seconds.");
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepositoryRoot()
    {
        foreach (string start in
            new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "dotnet",
                        "agent-framework-dotnet.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not find the Agent Framework repository root.");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal enum InterruptionKind
    {
        Crash,
        Shutdown,
    }
}
