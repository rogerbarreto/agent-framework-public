// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Context handed to a registered <see cref="IDurableTaskRunner"/> handler per invocation.
/// </summary>
/// <param name="Name">The handler name.</param>
/// <param name="Payload">The scheduled payload. For object-mode runners this is the original object reference.</param>
/// <param name="Attempt">1-based attempt counter; 1 on the initial call, &gt;1 on retries.</param>
/// <param name="State">
/// Mutable per-task state owned by the runner. Handlers may write cursors (e.g. <c>echo_done</c>)
/// here so a subsequent retry can detect partial progress and skip already-completed sub-steps.
/// </param>
public sealed record TaskInvocationContext(
    string Name,
    object Payload,
    int Attempt,
    IDictionary<string, object?> State);