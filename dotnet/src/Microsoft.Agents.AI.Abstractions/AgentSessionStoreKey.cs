// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI;

/// <summary>
/// Identifies an agent session in persistent storage.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SessionId"/> identifies the session while <see cref="Partitions"/> contains additional
/// named dimensions that isolate sessions sharing that identifier. Every partition is part of the
/// identity and must be honored by <see cref="AgentSessionStore"/> implementations.
/// </para>
/// <para>
/// Partition names are compared using ordinal, case-sensitive comparison. Partition ordering does not
/// affect identity. Names and values cannot be empty or whitespace.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
public sealed class AgentSessionStoreKey : IEquatable<AgentSessionStoreKey>
{
    private static readonly UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly int _hashCode;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentSessionStoreKey"/> class.
    /// </summary>
    /// <param name="sessionId">The logical session identifier.</param>
    /// <param name="partitions">
    /// Optional named partition values. Every partition contributes to identity. The collection is copied.
    /// </param>
    public AgentSessionStoreKey(
        string sessionId,
        IReadOnlyDictionary<string, string>? partitions = null)
    {
        this.SessionId = ValidateIdentityComponent(sessionId, nameof(sessionId));

        var partitionCopy = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (partitions is not null)
        {
            foreach (KeyValuePair<string, string> partition in partitions)
            {
                partitionCopy.Add(
                    ValidateIdentityComponent(partition.Key, nameof(partitions)),
                    ValidateIdentityComponent(partition.Value, nameof(partitions)));
            }
        }

        this.Partitions = new ReadOnlyDictionary<string, string>(partitionCopy);

        string canonicalValue = this.BuildCanonicalValue();
        this.StableStorageKey = ComputeStableStorageKey(canonicalValue);
        this._hashCode = StringComparer.Ordinal.GetHashCode(canonicalValue);
    }

    /// <summary>
    /// Gets the logical session identifier.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Gets the named partition values that form part of the session identity.
    /// </summary>
    public IReadOnlyDictionary<string, string> Partitions { get; }

    /// <summary>
    /// Returns a new key containing the specified partition.
    /// </summary>
    /// <param name="name">The partition name.</param>
    /// <param name="value">The partition value.</param>
    /// <returns>
    /// A new key with the partition added or replaced, or this instance when the partition already has
    /// the specified value.
    /// </returns>
    public AgentSessionStoreKey WithPartition(string name, string value)
    {
        name = ValidateIdentityComponent(name, nameof(name));
        value = ValidateIdentityComponent(value, nameof(value));

        if (this.Partitions.TryGetValue(name, out string? existingValue)
            && string.Equals(existingValue, value, StringComparison.Ordinal))
        {
            return this;
        }

        var partitions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> partition in this.Partitions)
        {
            partitions.Add(partition.Key, partition.Value);
        }
        partitions[name] = value;

        return new AgentSessionStoreKey(this.SessionId, partitions);
    }

    /// <summary>
    /// Gets a deterministic, opaque value suitable for addressing this key in persistent storage.
    /// </summary>
    /// <value>
    /// A versioned Base64URL-encoded SHA-256 hash of the session identifier and every partition.
    /// </value>
    /// <remarks>
    /// This value is stable across processes and does not expose the original session or partition values.
    /// </remarks>
    public string StableStorageKey { get; }

    /// <inheritdoc/>
    public bool Equals(AgentSessionStoreKey? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null
            || !string.Equals(this.SessionId, other.SessionId, StringComparison.Ordinal)
            || this.Partitions.Count != other.Partitions.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, string> partition in this.Partitions)
        {
            if (!other.Partitions.TryGetValue(partition.Key, out string? value)
                || !string.Equals(partition.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => this.Equals(obj as AgentSessionStoreKey);

    /// <inheritdoc/>
    public override int GetHashCode() => this._hashCode;

    private string BuildCanonicalValue()
    {
        StringBuilder builder = new();
        builder.Append("v1|s").Append(this.SessionId.Length).Append(':').Append(this.SessionId);
        builder.Append("|p").Append(this.Partitions.Count).Append('|');

        foreach (KeyValuePair<string, string> partition in this.Partitions)
        {
            builder.Append('n').Append(partition.Key.Length).Append(':').Append(partition.Key);
            builder.Append('v').Append(partition.Value.Length).Append(':').Append(partition.Value);
            builder.Append('|');
        }

        return builder.ToString();
    }

    private static string ComputeStableStorageKey(string canonicalValue)
    {
        byte[] input = s_strictUtf8.GetBytes(canonicalValue);
#if NET8_0_OR_GREATER
        byte[] hash = SHA256.HashData(input);
#else
        byte[] hash;
        using (SHA256 sha256 = SHA256.Create())
        {
            hash = sha256.ComputeHash(input);
        }
#endif
        return $"ask1_{Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    }

    private static string ValidateIdentityComponent(string value, string paramName)
    {
        value = Throw.IfNullOrWhitespace(value, paramName);

        try
        {
            _ = s_strictUtf8.GetByteCount(value);
            return value;
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Session key values must contain valid UTF-16 text.", paramName, exception);
        }
    }
}
