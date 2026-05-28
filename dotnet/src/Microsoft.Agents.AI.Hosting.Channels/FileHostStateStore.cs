// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// File-system-backed <see cref="IHostStateStore"/>. Each component (identity registry, link grants,
/// last-seen ledger, continuation tokens, session aliases) is persisted as one JSON file per record
/// under its configured path. Safe for single-process use; multiple processes sharing the same
/// directory require external coordination.
/// </summary>
/// <remarks>
/// In-memory caches are populated on first access and write-through to disk. The on-disk schema is
/// considered private to this implementation and may evolve. Mirrors the Python behaviour where
/// the host shipped a similar JSON-files store as the v1 default for long-running deployments.
/// </remarks>
[RequiresUnreferencedCode("FileHostStateStore uses reflection-based JSON serialization. Use a JsonTypeInfo-aware alternative for trimmed apps.")]
[RequiresDynamicCode("FileHostStateStore uses reflection-based JSON serialization. Use a JsonTypeInfo-aware alternative for AOT apps.")]
public sealed class FileHostStateStore : IHostStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly InMemoryHostStateStore _cache = new();
    private readonly string _linksPath;
    private readonly string _grantsPath;
    private readonly string _lastSeenPath;
    private readonly string _continuationsPath;
    private readonly string _aliasesPath;
    private readonly object _writeGate = new();
    private bool _hydrated;

    /// <summary>Initializes a new instance.</summary>
    public FileHostStateStore(HostStatePathOptions paths)
    {
        Throw.IfNull(paths);
        var root = paths.Root ?? "./.afhost";
        this._linksPath = paths.LinksPath ?? Path.Combine(root, "links");
        this._grantsPath = paths.LinksPath is not null ? Path.Combine(paths.LinksPath, "grants") : Path.Combine(root, "grants");
        this._lastSeenPath = paths.LastSeenPath ?? Path.Combine(root, "last-seen");
        this._continuationsPath = paths.ContinuationsPath ?? Path.Combine(root, "continuations");
        this._aliasesPath = Path.Combine(root, "aliases");

        Directory.CreateDirectory(this._linksPath);
        Directory.CreateDirectory(this._grantsPath);
        Directory.CreateDirectory(this._lastSeenPath);
        Directory.CreateDirectory(this._continuationsPath);
        Directory.CreateDirectory(this._aliasesPath);
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetIsolationKeyAsync(ChannelIdentity identity, CancellationToken cancellationToken)
    {
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        return await this._cache.GetIsolationKeyAsync(identity, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask SaveLinkAsync(
        ChannelIdentity identity,
        string isolationKey,
        IReadOnlyDictionary<string, string>? verifiedClaims,
        CancellationToken cancellationToken)
    {
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        await this._cache.SaveLinkAsync(identity, isolationKey, verifiedClaims, cancellationToken).ConfigureAwait(false);
        var snapshot = await this._cache.GetIdentitiesAsync(isolationKey, cancellationToken).ConfigureAwait(false);
        this.WriteJson(Path.Combine(this._linksPath, EncodeFileName(isolationKey) + ".json"), snapshot);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ChannelIdentityRegistration>> GetIdentitiesAsync(string isolationKey, CancellationToken cancellationToken)
    {
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        return await this._cache.GetIdentitiesAsync(isolationKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<string?> LookupByVerifiedClaimAsync(string claim, string value, CancellationToken cancellationToken)
    {
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        return await this._cache.LookupByVerifiedClaimAsync(claim, value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask SaveLinkGrantAsync(LinkGrant grant, CancellationToken cancellationToken)
    {
        Throw.IfNull(grant);
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        await this._cache.SaveLinkGrantAsync(grant, cancellationToken).ConfigureAwait(false);
        this.WriteJson(Path.Combine(this._grantsPath, EncodeFileName(grant.Code) + ".json"), grant);
    }

    /// <inheritdoc />
    public async ValueTask<LinkGrant?> GetLinkGrantAsync(string code, CancellationToken cancellationToken)
    {
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        return await this._cache.GetLinkGrantAsync(code, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<LinkGrant?> ConsumeLinkGrantAsync(string code, CancellationToken cancellationToken)
    {
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        var consumed = await this._cache.ConsumeLinkGrantAsync(code, cancellationToken).ConfigureAwait(false);
        if (consumed is not null)
        {
            this.DeleteIfExists(Path.Combine(this._grantsPath, EncodeFileName(code) + ".json"));
        }
        return consumed;
    }

    /// <inheritdoc />
    public async ValueTask RecordLastSeenAsync(
        string isolationKey,
        ChannelIdentity identity,
        string? conversationId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        await this._cache.RecordLastSeenAsync(isolationKey, identity, conversationId, at, cancellationToken).ConfigureAwait(false);
        var record = await this._cache.GetLastSeenAsync(isolationKey, cancellationToken).ConfigureAwait(false);
        if (record is not null)
        {
            this.WriteJson(Path.Combine(this._lastSeenPath, EncodeFileName(isolationKey) + ".json"), record);
        }
    }

    /// <inheritdoc />
    public async ValueTask<LastSeenRecord?> GetLastSeenAsync(string isolationKey, CancellationToken cancellationToken)
    {
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        return await this._cache.GetLastSeenAsync(isolationKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask SaveContinuationAsync(ContinuationToken token, CancellationToken cancellationToken)
    {
        Throw.IfNull(token);
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        await this._cache.SaveContinuationAsync(token, cancellationToken).ConfigureAwait(false);
        this.WriteJson(Path.Combine(this._continuationsPath, EncodeFileName(token.Token) + ".json"), token);
    }

    /// <inheritdoc />
    public async ValueTask<ContinuationToken?> GetContinuationAsync(string token, CancellationToken cancellationToken)
    {
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        return await this._cache.GetContinuationAsync(token, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DeleteContinuationAsync(string token, CancellationToken cancellationToken)
    {
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        await this._cache.DeleteContinuationAsync(token, cancellationToken).ConfigureAwait(false);
        this.DeleteIfExists(Path.Combine(this._continuationsPath, EncodeFileName(token) + ".json"));
    }

    /// <inheritdoc />
    public async ValueTask RotateSessionAliasAsync(string isolationKey, CancellationToken cancellationToken)
    {
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        await this._cache.RotateSessionAliasAsync(isolationKey, cancellationToken).ConfigureAwait(false);
        var alias = await this._cache.GetActiveSessionAliasAsync(isolationKey, cancellationToken).ConfigureAwait(false);
        if (alias is not null)
        {
            this.WriteJson(Path.Combine(this._aliasesPath, EncodeFileName(isolationKey) + ".json"), alias);
        }
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetActiveSessionAliasAsync(string isolationKey, CancellationToken cancellationToken)
    {
        await this.HydrateAsync(cancellationToken).ConfigureAwait(false);
        var alias = await this._cache.GetActiveSessionAliasAsync(isolationKey, cancellationToken).ConfigureAwait(false);
        if (alias is not null && !File.Exists(Path.Combine(this._aliasesPath, EncodeFileName(isolationKey) + ".json")))
        {
            this.WriteJson(Path.Combine(this._aliasesPath, EncodeFileName(isolationKey) + ".json"), alias);
        }
        return alias;
    }

    private async ValueTask HydrateAsync(CancellationToken cancellationToken)
    {
        if (this._hydrated) { return; }
        lock (this._writeGate)
        {
            if (this._hydrated) { return; }
            this._hydrated = true;
        }

        foreach (var file in Directory.EnumerateFiles(this._linksPath, "*.json"))
        {
            var snapshot = ReadJson<List<ChannelIdentityRegistration>>(file);
            if (snapshot is null) { continue; }
            var isolationKey = DecodeFileName(Path.GetFileNameWithoutExtension(file));
            foreach (var reg in snapshot)
            {
                await this._cache.SaveLinkAsync(reg.Identity, isolationKey, reg.VerifiedClaims, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var file in Directory.EnumerateFiles(this._grantsPath, "*.json"))
        {
            var grant = ReadJson<LinkGrant>(file);
            if (grant is null) { continue; }
            if (grant.ExpiresAt <= DateTimeOffset.UtcNow) { this.DeleteIfExists(file); continue; }
            await this._cache.SaveLinkGrantAsync(grant, cancellationToken).ConfigureAwait(false);
        }

        foreach (var file in Directory.EnumerateFiles(this._lastSeenPath, "*.json"))
        {
            var record = ReadJson<LastSeenRecord>(file);
            if (record is null) { continue; }
            var isolationKey = DecodeFileName(Path.GetFileNameWithoutExtension(file));
            await this._cache.RecordLastSeenAsync(isolationKey, record.Identity, record.ConversationId, record.At, cancellationToken).ConfigureAwait(false);
        }

        foreach (var file in Directory.EnumerateFiles(this._continuationsPath, "*.json"))
        {
            var token = ReadJson<ContinuationToken>(file);
            if (token is null) { continue; }
            await this._cache.SaveContinuationAsync(token, cancellationToken).ConfigureAwait(false);
        }
    }

    private void WriteJson<T>(string path, T payload)
    {
        lock (this._writeGate)
        {
            using var stream = File.Create(path);
            JsonSerializer.Serialize(stream, payload, JsonOptions);
        }
    }

    private static T? ReadJson<T>(string path) where T : class
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void DeleteIfExists(string path)
    {
        lock (this._writeGate)
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    private static string EncodeFileName(string raw)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
        return Convert.ToHexString(bytes);
    }

    private static string DecodeFileName(string encoded)
    {
        var bytes = Convert.FromHexString(encoded);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}