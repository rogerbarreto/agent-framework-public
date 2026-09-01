// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI.Hosting;

/// <summary>
/// A delegating <see cref="AgentSessionStore"/> that supplies the per-user partition key from an
/// <see cref="AgentIsolationKeyProvider"/>.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
public class IsolationKeyScopedAgentSessionStore : DelegatingAgentSessionStore
{
    private readonly AgentIsolationKeyProvider? _keyProvider;
    private readonly bool _strict;

    /// <summary>
    /// Initializes a new instance of the <see cref="IsolationKeyScopedAgentSessionStore"/> class.
    /// </summary>
    /// <param name="innerStore">The underlying <see cref="AgentSessionStore"/> to delegate to.</param>
    /// <param name="keyProvider">
    /// The <see cref="AgentIsolationKeyProvider"/> used to retrieve the isolation key for the current context.
    /// </param>
    /// <param name="options">The options for configuring the session store. If null, defaults are used.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="innerStore"/> is <see langword="null"/>.
    /// </exception>
    public IsolationKeyScopedAgentSessionStore(
        AgentSessionStore innerStore,
        AgentIsolationKeyProvider? keyProvider,
        IsolationKeyScopedAgentSessionStoreOptions? options = null)
        : base(innerStore)
    {
        this._keyProvider = keyProvider;
        options ??= new IsolationKeyScopedAgentSessionStoreOptions();
        this._strict = options.Strict;
    }

    /// <summary>
    /// Asynchronously retrieves the isolation key from the provider and validates it if in strict mode.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The isolation key string, or <see langword="null"/> if no key is available and non-strict mode is enabled.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The provider returned <see langword="null"/> and strict mode is enabled.
    /// </exception>
    private async ValueTask<string?> GetIsolationKeyAsync(CancellationToken cancellationToken)
    {
        string? key = this._keyProvider != null
                    ? await this._keyProvider.GetIsolationKeyAsync(cancellationToken).ConfigureAwait(false)
                    : null;

        if (string.IsNullOrWhiteSpace(key))
        {
            if (this._strict)
            {
                throw new InvalidOperationException("Agent isolation key is required but was not provided by the configured AgentIsolationKeyProvider.");
            }

            return null;
        }

        return key;
    }

    /// <summary>
    /// Resolves the user partition passed to the inner store. A key supplied by the provider takes precedence
    /// over the caller value because it represents the current hosting context.
    /// </summary>
    private async ValueTask<string?> GetUserIdAsync(string? userId, CancellationToken cancellationToken)
        => await this.GetIsolationKeyAsync(cancellationToken).ConfigureAwait(false) ?? userId;

    /// <inheritdoc />
    public override async ValueTask<AgentSession?> GetSessionAsync(
        AIAgent agent,
        string conversationId,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        string? resolvedUserId = await this.GetUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        return await this.InnerStore.GetSessionAsync(agent, conversationId, resolvedUserId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask<AgentSession> GetOrCreateSessionAsync(
        AIAgent agent,
        string conversationId,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        string? resolvedUserId = await this.GetUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        return await this.InnerStore.GetOrCreateSessionAsync(agent, conversationId, resolvedUserId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask SaveSessionAsync(
        AIAgent agent,
        string conversationId,
        AgentSession session,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        string? resolvedUserId = await this.GetUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        await this.InnerStore.SaveSessionAsync(agent, conversationId, session, resolvedUserId, cancellationToken).ConfigureAwait(false);
    }
}
