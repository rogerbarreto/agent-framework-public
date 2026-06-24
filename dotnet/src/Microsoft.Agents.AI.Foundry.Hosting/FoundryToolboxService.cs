// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Shared.DiagnosticIds;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// An <see cref="IHostedService"/> that eagerly connects to the Foundry Toolboxes MCP proxy at
/// container startup, discovers tools via <c>tools/list</c>, and caches them so they can be
/// injected into every <see cref="ChatOptions"/> by <see cref="AgentFrameworkResponseHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// The toolbox proxy base URL is derived from the platform-injected
/// <c>FOUNDRY_PROJECT_ENDPOINT</c> environment variable per <c>tools-integration-spec.md</c>
/// §2–§3. The per-toolbox proxy URL is constructed as
/// <c>{FOUNDRY_PROJECT_ENDPOINT}/toolboxes/{toolboxName}/mcp?api-version={ApiVersion}</c>.
/// </para>
/// <para>
/// When <c>FOUNDRY_PROJECT_ENDPOINT</c> is absent the service starts without error and
/// no tools are registered, keeping the container healthy per spec §2.
/// </para>
/// <para>
/// Startup eagerly connects to every name in <see cref="FoundryToolboxOptions.ToolboxNames"/>.
/// Beyond those, per-request toolbox markers (see <see cref="HostedMcpToolboxAITool"/>) are
/// resolved at request time through <see cref="GetToolboxToolsAsync"/>. Unknown toolboxes are
/// rejected when <see cref="FoundryToolboxOptions.StrictMode"/> is <see langword="true"/> and
/// lazily connected otherwise.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]
public sealed class FoundryToolboxService : IHostedService, IAsyncDisposable
{
    private readonly FoundryToolboxOptions _options;
    private readonly TokenCredential _credential;
    private readonly ILogger<FoundryToolboxService> _logger;

    private readonly Dictionary<string, CachedToolbox> _toolboxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<McpConsentInfo>> _pendingConsents = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deferredToolboxNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lazyOpenLock = new(1, 1);

    // Immutable snapshot of _pendingConsents, rebuilt in RecomputeStatus under _lazyOpenLock so
    // GetPendingConsents can return it without enumerating the live dictionary off-lock.
    private IReadOnlyList<McpConsentInfo> _pendingConsentsSnapshot = [];

    private string? _resolvedEndpoint;
    private string? _featuresHeader;
    private string _agentName = "hosted-agent";
    private string _agentVersion = "1.0.0";

    /// <summary>
    /// Gets the cached list of <see cref="AITool"/> instances discovered from all
    /// pre-registered toolboxes. Always non-null after startup.
    /// </summary>
    public IReadOnlyList<AITool> Tools { get; private set; } = [];

    /// <summary>
    /// Gets the startup status of the service. Reflects the outcome of pre-registered
    /// toolbox connections opened in <see cref="StartAsync"/>; lazy-opens triggered by
    /// per-request markers do not change this value.
    /// </summary>
    /// <remarks>
    /// Consumed by <see cref="FoundryToolboxHealthCheck"/> to gate the
    /// <c>GET /readiness</c> probe so the Foundry hosted runtime does not start routing
    /// traffic to a container whose pre-registered toolbox failed to open at startup.
    /// </remarks>
    public FoundryToolboxStartupStatus StartupStatus { get; private set; } = FoundryToolboxStartupStatus.Pending;

    /// <summary>
    /// Gets the names of pre-registered toolboxes that failed to open during
    /// <see cref="StartAsync"/>. Empty when startup was successful or has not run yet.
    /// </summary>
    public IReadOnlyList<string> FailedToolboxNames { get; private set; } = [];

    /// <summary>
    /// Gets the names of pre-registered toolboxes whose tools could not be enumerated at
    /// startup because a tool source requires user OAuth consent. Such toolboxes keep the
    /// container routable (see <see cref="FoundryToolboxStartupStatus.ConsentRequired"/>); the
    /// consent requirement is surfaced per-request and enumeration is retried via
    /// <see cref="ResolvePendingConsentsAsync"/> once the user has consented.
    /// </summary>
    public IReadOnlyList<string> ConsentRequiredToolboxNames { get; private set; } = [];

    /// <summary>
    /// Gets the names of pre-registered toolboxes that could not be enumerated at startup due to a
    /// non-consent error (for example, a tool source that requires a per-user delegated identity
    /// which is only available on a user request's egress). Such toolboxes keep the container
    /// routable (see <see cref="FoundryToolboxStartupStatus.Degraded"/>) and are retried per-request
    /// via <see cref="RetryDeferredToolboxesAsync"/>, where the platform-injected per-user isolation
    /// key is present on the toolbox proxy egress.
    /// </summary>
    public IReadOnlyList<string> DeferredToolboxNames { get; private set; } = [];

    /// <summary>
    /// Initializes a new instance of <see cref="FoundryToolboxService"/>.
    /// </summary>
    public FoundryToolboxService(
        IOptions<FoundryToolboxOptions> options,
        TokenCredential credential,
        ILogger<FoundryToolboxService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credential);

        this._options = options.Value;
        this._credential = credential;
        this._logger = logger ?? NullLogger<FoundryToolboxService>.Instance;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Per tools-integration-spec.md §2-§3, the container derives the toolbox proxy base
        // URL from the platform-injected FOUNDRY_PROJECT_ENDPOINT. The EndpointOverride
        // option exists for tests; AZURE_AI_PROJECT_ENDPOINT is honored as a local-dev
        // fallback to mirror the convention used by AF-repo samples.
        var projectEndpoint = this._options.EndpointOverride
            ?? Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT");

        if (string.IsNullOrEmpty(projectEndpoint))
        {
            this._logger.LogWarning(
                "Neither FOUNDRY_PROJECT_ENDPOINT nor AZURE_AI_PROJECT_ENDPOINT is set; toolbox support is disabled.");
            this.Tools = [];
            this.StartupStatus = FoundryToolboxStartupStatus.NoEndpoint;
            return;
        }

        this._resolvedEndpoint = projectEndpoint.TrimEnd('/');
        this._featuresHeader = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_TOOLSET_FEATURES");
        this._agentName = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_NAME") ?? "hosted-agent";
        this._agentVersion = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_VERSION") ?? "1.0.0";

        if (this._options.ToolboxNames.Count == 0)
        {
            this._logger.LogInformation("No pre-registered toolbox names configured.");
            this.Tools = [];
            this.StartupStatus = FoundryToolboxStartupStatus.Healthy;
            return;
        }

        var allTools = new List<AITool>();
        var deferred = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var toolboxName in this._options.ToolboxNames)
        {
            if (!seen.Add(toolboxName))
            {
                continue;
            }

            try
            {
                var result = await this.OpenToolboxAsync(toolboxName, version: null, cancellationToken).ConfigureAwait(false);
                if (result.Consents is { } consents)
                {
                    // The toolbox could not enumerate because a tool source needs user OAuth consent.
                    // Keep the container routable: record the requirement so the handler can surface it
                    // per-request, and retry enumeration once the user has consented.
                    this._pendingConsents[toolboxName] = consents;

                    if (this._logger.IsEnabled(LogLevel.Information))
                    {
                        this._logger.LogInformation(
                            "Toolbox '{ToolboxName}' requires user OAuth consent before its tools can be enumerated. " +
                            "The container stays ready; the consent prompt is surfaced to the caller per-request.",
                            toolboxName);
                    }

                    continue;
                }

                var cached = result.Cached!;
                this._toolboxes[toolboxName] = cached;
                allTools.AddRange(cached.Tools);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A non-consent failure at startup is often not permanent: the toolbox may require a
                // per-user (delegated) identity that is only present on a user request's egress,
                // where the platform stamps the per-user isolation key. Startup enumeration runs as
                // the container's managed identity with no user context, so a delegated-only tool
                // source (for example a Microsoft Graph / Agent365 connection) cannot list its tools
                // yet. Rather than failing readiness and bricking the container, keep it routable and
                // defer the toolbox: it is retried per-request via RetryDeferredToolboxesAsync, where
                // the user context is available and enumeration can succeed (or surface consent).
                if (this._logger.IsEnabled(LogLevel.Warning))
                {
                    this._logger.LogWarning(
                        ex,
                        "Toolbox '{ToolboxName}' could not be enumerated at startup; deferring it to per-request resolution. " +
                        "The container stays ready and the toolbox is retried on the next request when the per-user context is available.",
                        toolboxName);
                }

                deferred.Add(toolboxName);
            }
        }

        this.Tools = allTools;
        foreach (var name in deferred)
        {
            this._deferredToolboxNames.Add(name);
        }

        this.FailedToolboxNames = [];
        this.RecomputeStatus();
    }

    /// <summary>
    /// Recomputes <see cref="StartupStatus"/> and refreshes <see cref="ConsentRequiredToolboxNames"/>
    /// and <see cref="DeferredToolboxNames"/> from the current failed, pending-consent and deferred
    /// sets. This is the single point that derives the public consent/deferred snapshots from
    /// <c>_pendingConsents</c> and <c>_deferredToolboxNames</c>, so every mutation of those sets only
    /// needs to call this method. A hard failure dominates; otherwise an outstanding consent
    /// requirement keeps the container routable via
    /// <see cref="FoundryToolboxStartupStatus.ConsentRequired"/>; a deferred toolbox keeps it routable
    /// via <see cref="FoundryToolboxStartupStatus.Degraded"/>; otherwise Healthy.
    /// </summary>
    private void RecomputeStatus()
    {
        this.ConsentRequiredToolboxNames = [.. this._pendingConsents.Keys];
        this.DeferredToolboxNames = [.. this._deferredToolboxNames];

        // Flatten the pending-consent map to an immutable snapshot here, while we hold the lock
        // (RecomputeStatus is only ever called under _lazyOpenLock). GetPendingConsents then returns
        // this snapshot without touching the live dictionary, so concurrent requests cannot observe a
        // mutating collection.
        if (this._pendingConsents.Count == 0)
        {
            this._pendingConsentsSnapshot = [];
        }
        else
        {
            var snapshot = new List<McpConsentInfo>();
            foreach (var consents in this._pendingConsents.Values)
            {
                snapshot.AddRange(consents);
            }

            this._pendingConsentsSnapshot = snapshot;
        }

        this.StartupStatus = this.FailedToolboxNames.Count > 0
            ? FoundryToolboxStartupStatus.Unhealthy
            : this._pendingConsents.Count > 0
                ? FoundryToolboxStartupStatus.ConsentRequired
                : this._deferredToolboxNames.Count > 0
                    ? FoundryToolboxStartupStatus.Degraded
                    : FoundryToolboxStartupStatus.Healthy;
    }

    /// <summary>
    /// Returns a snapshot of the consent requirements currently outstanding across all toolboxes
    /// (pre-registered and per-request markers), flattened to one entry per consent-gated tool source.
    /// Does not retry or open anything; the caller surfaces each entry as an
    /// <c>oauth_consent_request</c>. Empty when nothing is awaiting consent.
    /// </summary>
    internal IReadOnlyList<McpConsentInfo> GetPendingConsents() => this._pendingConsentsSnapshot;

    /// <summary>
    /// Retries enumeration for any pre-registered toolbox that was awaiting user OAuth consent at
    /// startup. Call this at the start of request handling: once the user has completed consent
    /// out of band, the proxy holds a valid token and <c>tools/list</c> now succeeds, so the
    /// toolbox's tools become available and are appended to <see cref="Tools"/>.
    /// </summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>
    /// The consent requirements that are still outstanding (one per consent-gated tool source).
    /// Empty when nothing is pending or every pending toolbox resolved. When non-empty, the caller
    /// should surface each entry as an <c>oauth_consent_request</c> and stop, so the user can
    /// complete consent and re-send the request.
    /// </returns>
    internal async ValueTask<IReadOnlyList<McpConsentInfo>> ResolvePendingConsentsAsync(CancellationToken cancellationToken)
    {
        // Fast path: nothing awaiting consent.
        if (this.ConsentRequiredToolboxNames.Count == 0)
        {
            return [];
        }

        await this._lazyOpenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this._pendingConsents.Count == 0)
            {
                return [];
            }

            var stillPending = new List<McpConsentInfo>();
            var resolvedTools = new List<AITool>();

            foreach (var toolboxName in new List<string>(this._pendingConsents.Keys))
            {
                try
                {
                    var result = await this.OpenToolboxAsync(toolboxName, version: null, cancellationToken).ConfigureAwait(false);
                    if (result.Consents is { } consents)
                    {
                        // Still gated: refresh the consent info (the URL may rotate) and surface it.
                        this._pendingConsents[toolboxName] = consents;
                        stillPending.AddRange(consents);
                        continue;
                    }

                    var cached = result.Cached!;
                    this._toolboxes[toolboxName] = cached;
                    resolvedTools.AddRange(cached.Tools);
                    this._pendingConsents.Remove(toolboxName);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Transient/other error: leave the toolbox pending so a later request retries.
                    // Do not surface a consent prompt (we have no URL); proceed without these tools.
                    if (this._logger.IsEnabled(LogLevel.Warning))
                    {
                        this._logger.LogWarning(
                            ex,
                            "Retry of consent-gated toolbox '{ToolboxName}' failed; it remains pending.",
                            toolboxName);
                    }
                }
            }

            if (resolvedTools.Count > 0)
            {
                this.Tools = [.. this.Tools, .. resolvedTools];
            }

            this.RecomputeStatus();

            return stillPending;
        }
        finally
        {
            this._lazyOpenLock.Release();
        }
    }

    /// <summary>
    /// Retries enumeration for any pre-registered toolbox that could not be opened at startup due to
    /// a non-consent error (recorded in <see cref="DeferredToolboxNames"/>). Call this at the start of
    /// request handling, before <see cref="ResolvePendingConsentsAsync"/>: the request's egress carries
    /// the platform-injected per-user isolation key, so a toolbox that needs a delegated user identity
    /// (for example a Microsoft Graph / Agent365 connection) can now enumerate as that user. On success
    /// the toolbox's tools are appended to <see cref="Tools"/>; if the proxy now reports the source needs
    /// user OAuth consent, the toolbox is moved to the pending-consent set so the caller surfaces the
    /// consent prompt; if it still fails, it stays deferred and is retried on a later request.
    /// </summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    internal async ValueTask RetryDeferredToolboxesAsync(CancellationToken cancellationToken)
    {
        // Fast path: nothing deferred.
        if (this._deferredToolboxNames.Count == 0)
        {
            return;
        }

        await this._lazyOpenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this._deferredToolboxNames.Count == 0)
            {
                return;
            }

            var resolvedTools = new List<AITool>();

            foreach (var toolboxName in new List<string>(this._deferredToolboxNames))
            {
                try
                {
                    var result = await this.OpenToolboxAsync(toolboxName, version: null, cancellationToken).ConfigureAwait(false);
                    if (result.Consents is { } consents)
                    {
                        // With the per-user context now present, the proxy reports the tool source
                        // needs user OAuth consent. Move it to the pending-consent set (the handler
                        // surfaces the prompt) and drop it from the deferred set.
                        this._pendingConsents[toolboxName] = consents;
                        this._deferredToolboxNames.Remove(toolboxName);
                        continue;
                    }

                    var cached = result.Cached!;
                    this._toolboxes[toolboxName] = cached;
                    resolvedTools.AddRange(cached.Tools);
                    this._deferredToolboxNames.Remove(toolboxName);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Still failing — keep it deferred and retry on a later request.
                    if (this._logger.IsEnabled(LogLevel.Warning))
                    {
                        this._logger.LogWarning(
                            ex,
                            "Retry of deferred toolbox '{ToolboxName}' failed; it remains deferred.",
                            toolboxName);
                    }
                }
            }

            if (resolvedTools.Count > 0)
            {
                this.Tools = [.. this.Tools, .. resolvedTools];
            }

            this.RecomputeStatus();
        }
        finally
        {
            this._lazyOpenLock.Release();
        }
    }

    /// <summary>
    /// Resolves the tools for a per-request toolbox marker. Returns cached tools when the
    /// toolbox has already been opened; otherwise honors
    /// <see cref="FoundryToolboxOptions.StrictMode"/> to either reject or lazily open it.
    /// </summary>
    /// <param name="toolboxName">The Foundry toolbox name from the marker.</param>
    /// <param name="version">
    /// Optional pinned version. Currently reserved for future use — version-specific routing is
    /// handled server-side by the Foundry proxy. This parameter is accepted for forward compatibility
    /// but does not affect the proxy URL used to connect to the toolbox.
    /// </param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the toolbox is not pre-registered and <see cref="FoundryToolboxOptions.StrictMode"/>
    /// is <see langword="true"/>, or when the toolbox endpoint is not configured.
    /// </exception>
    public async ValueTask<IReadOnlyList<AITool>> GetToolboxToolsAsync(
        string toolboxName,
        string? version,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolboxName);

        if (this._toolboxes.TryGetValue(toolboxName, out var cached))
        {
            return cached.Tools;
        }

        if (this._options.StrictMode && !this._options.ToolboxNames.Contains(toolboxName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Toolbox '{toolboxName}' is not pre-registered via AddFoundryToolboxes(...). " +
                $"Either register it at startup or set {nameof(FoundryToolboxOptions.StrictMode)}=false to allow lazy resolution.");
        }

        if (string.IsNullOrEmpty(this._resolvedEndpoint))
        {
            throw new InvalidOperationException(
                $"Cannot resolve toolbox '{toolboxName}': FOUNDRY_PROJECT_ENDPOINT is not set.");
        }

        await this._lazyOpenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring the lock to avoid duplicate opens under concurrency.
            if (this._toolboxes.TryGetValue(toolboxName, out cached))
            {
                return cached.Tools;
            }

            var result = await this.OpenToolboxAsync(toolboxName, version, cancellationToken).ConfigureAwait(false);
            if (result.Consents is { } pendingConsents)
            {
                // The toolbox needs user OAuth consent before its tools can be enumerated.
                // Record it as pending so the request handler can surface the consent prompt,
                // and return no tools for this turn rather than failing the request.
                this._pendingConsents[toolboxName] = pendingConsents;
                this.RecomputeStatus();
                return [];
            }

            cached = result.Cached!;
            this._toolboxes[toolboxName] = cached;
            return cached.Tools;
        }
        finally
        {
            this._lazyOpenLock.Release();
        }
    }

    private async Task<ToolboxOpenResult> OpenToolboxAsync(
        string toolboxName,
        string? version,
        CancellationToken cancellationToken)
    {
        var proxyUrl = $"{this._resolvedEndpoint!}/toolboxes/{toolboxName}/mcp?api-version={this._options.ApiVersion}";

        if (this._logger.IsEnabled(LogLevel.Information))
        {
            this._logger.LogInformation("Connecting to toolbox '{ToolboxName}' at {ProxyUrl}.", toolboxName, proxyUrl);
        }

        // Build the endpoint URI before allocating the HttpClient so a malformed URL cannot leak it.
        var endpoint = new Uri(proxyUrl);

        var handler = new FoundryToolboxBearerTokenHandler(this._credential, this._featuresHeader)
        {
            InnerHandler = new HttpClientHandler()
        };

        var httpClient = new HttpClient(handler);

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            Name = toolboxName,
        };

        var transport = new HttpClientTransport(transportOptions, httpClient);

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new()
            {
                Name = this._agentName,
                Version = this._agentVersion
            }
        };

        // McpClient.CreateAsync runs the MCP initialize handshake and can throw for an unreachable
        // proxy (the deferred-toolbox case, retried per request). Keep it inside the try so the
        // HttpClient is always disposed on failure rather than leaking a socket on every retry.
        McpClient? client = null;
        IList<McpClientTool> mcpTools;
        try
        {
            client = await McpClient.CreateAsync(
                transport,
                clientOptions,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            mcpTools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (McpProtocolException ex) when (
            ToolboxConsentParser.TryParseConsentRequired(toolboxName, ex.Message, out var consents))
        {
            // A tool source needs user OAuth consent before it can be enumerated. Dispose the
            // half-open client and signal the caller, which keeps the container routable and
            // surfaces the consent prompt per-request instead of failing readiness.
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            httpClient.Dispose();
            return new ToolboxOpenResult(Cached: null, Consents: consents);
        }
        catch
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            httpClient.Dispose();
            throw;
        }

        if (this._logger.IsEnabled(LogLevel.Information))
        {
            this._logger.LogInformation(
                "Toolbox '{ToolboxName}': discovered {ToolCount} tool(s).",
                toolboxName,
                mcpTools.Count);
        }

        var wrapped = new List<AITool>(mcpTools.Count);
        foreach (var tool in mcpTools)
        {
            wrapped.Add(new ConsentAwareMcpClientAIFunction(tool, toolboxName));
        }

        _ = version; // reserved for future version-specific routing; currently handled server-side by the proxy.

        return new ToolboxOpenResult(new CachedToolbox(client!, httpClient, wrapped), Consents: null);
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var cached in this._toolboxes.Values)
        {
            await cached.Client.DisposeAsync().ConfigureAwait(false);
            cached.HttpClient.Dispose();
        }

        this._toolboxes.Clear();
        this._lazyOpenLock.Dispose();
    }

    private sealed record CachedToolbox(McpClient Client, HttpClient HttpClient, IReadOnlyList<AITool> Tools);

    /// <summary>
    /// Outcome of an <see cref="OpenToolboxAsync"/> attempt. Exactly one of the two values is set:
    /// <see cref="Cached"/> when the toolbox opened and its tools were enumerated, or
    /// <see cref="Consents"/> when enumeration is blocked pending user OAuth consent.
    /// </summary>
    private sealed record ToolboxOpenResult(CachedToolbox? Cached, IReadOnlyList<McpConsentInfo>? Consents);
}
