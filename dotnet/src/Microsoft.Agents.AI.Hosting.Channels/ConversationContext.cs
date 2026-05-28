// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Conversation-shape hints handed to the authorization pipeline.
/// </summary>
/// <param name="ConversationId">The protocol-visible conversation id, or <see langword="null"/> for 1:1.</param>
/// <param name="IsGroup">Whether this conversation has more than one human participant.</param>
public sealed record ConversationContext(string? ConversationId, bool IsGroup);