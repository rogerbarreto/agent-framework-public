// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// File-system-backed <see cref="IHostStateStore"/>. Persists reset-session aliases as one file per
/// isolation key and derives per-isolation-key workflow checkpoint directories. Safe for single-process
/// use; multiple processes sharing the same directory require external coordination.
/// </summary>
public sealed class FileHostStateStore : IHostStateStore
{
    private readonly ConcurrentDictionary<string, string> _aliasCache = new(StringComparer.Ordinal);
    private readonly string _aliasesPath;
    private readonly string _checkpointsPath;
    private readonly object _gate = new();

    /// <summary>Initializes a new instance.</summary>
    public FileHostStateStore(HostStatePathOptions paths)
    {
        Throw.IfNull(paths);
        var root = paths.Root ?? "./.afhost";
        this._aliasesPath = paths.AliasesPath ?? Path.Combine(root, "aliases");
        this._checkpointsPath = paths.CheckpointsPath ?? Path.Combine(root, "checkpoints");
        Directory.CreateDirectory(this._aliasesPath);
        Directory.CreateDirectory(this._checkpointsPath);
    }

    /// <inheritdoc />
    public ValueTask RotateSessionAliasAsync(string isolationKey, CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(isolationKey);
        var alias = Guid.NewGuid().ToString("N");
        this._aliasCache[isolationKey] = alias;
        lock (this._gate)
        {
            File.WriteAllText(this.AliasFile(isolationKey), alias);
        }
        return default;
    }

    /// <inheritdoc />
    public ValueTask<string?> GetActiveSessionAliasAsync(string isolationKey, CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(isolationKey);
        if (this._aliasCache.TryGetValue(isolationKey, out var cached))
        {
            return new(cached);
        }

        var file = this.AliasFile(isolationKey);
        string alias;
        lock (this._gate)
        {
            alias = File.Exists(file) ? File.ReadAllText(file) : isolationKey;
            if (!File.Exists(file))
            {
                File.WriteAllText(file, alias);
            }
        }
        this._aliasCache[isolationKey] = alias;
        return new(alias);
    }

    /// <inheritdoc />
    public ValueTask<string> GetCheckpointLocationAsync(string isolationKey, CancellationToken cancellationToken)
    {
        Throw.IfNullOrEmpty(isolationKey);
        var dir = Path.Combine(this._checkpointsPath, EncodeFileName(isolationKey));
        Directory.CreateDirectory(dir);
        return new(dir);
    }

    private string AliasFile(string isolationKey) => Path.Combine(this._aliasesPath, EncodeFileName(isolationKey) + ".txt");

    private static string EncodeFileName(string raw) => Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(raw));
}
