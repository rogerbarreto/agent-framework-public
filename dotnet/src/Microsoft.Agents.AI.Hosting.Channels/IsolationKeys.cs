// Copyright (c) Microsoft. All rights reserved.

using System.Threading;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Per-request partition hints carried via <see cref="AsyncLocal{T}"/>. Distinct from the app-level
/// isolation key produced by <see cref="IIdentityLinker"/>; this is the Foundry runtime's
/// per-request partition hint lifted off <c>x-agent-user-isolation-key</c> /
/// <c>x-agent-chat-isolation-key</c> headers.
/// </summary>
public sealed record IsolationKeys(string? UserKey, string? ChatKey)
{
    /// <summary>The async-local slot.</summary>
    public static AsyncLocal<IsolationKeys?> CurrentSlot { get; } = new();

    /// <summary>The current per-request value, if any.</summary>
    public static IsolationKeys? Current
    {
        get => CurrentSlot.Value;
        set => CurrentSlot.Value = value;
    }

    /// <summary>True when both keys are <see langword="null"/>.</summary>
    public bool IsEmpty => this.UserKey is null && this.ChatKey is null;

    /// <summary>Header name for the user key.</summary>
    public const string UserHeader = "x-agent-user-isolation-key";

    /// <summary>Header name for the chat key.</summary>
    public const string ChatHeader = "x-agent-chat-isolation-key";
}

/// <summary>DI wrapper around <see cref="IsolationKeys.Current"/> for testability.</summary>
public interface IIsolationKeysAccessor
{
    /// <summary>Returns the current per-request keys.</summary>
    IsolationKeys? Current { get; }
}

internal sealed class IsolationKeysAccessor : IIsolationKeysAccessor
{
    public IsolationKeys? Current => IsolationKeys.Current;
}