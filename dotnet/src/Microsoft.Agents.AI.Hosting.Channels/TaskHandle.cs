// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Opaque handle to a task scheduled on <see cref="IDurableTaskRunner"/>.
/// </summary>
/// <param name="TaskId">The runner-assigned task identifier.</param>
/// <param name="Name">The handler name the task was scheduled under.</param>
public sealed record TaskHandle(string TaskId, string Name);