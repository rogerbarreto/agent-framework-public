// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.Agents.AI.UnitTests.ChatClient;

/// <summary>
/// Test helper that captures <see cref="Activity"/> instances on the AgentFramework default
/// source while filtering by ownership. Each capture instance only collects activities whose
/// parent chain contains the <see cref="OpenTelemetryConsts.OwnedInvokeAgentScopeMarker"/>
/// custom property pointing at the specified owner <see cref="ChatClientAgent"/>. This makes
/// tests parallel-safe: two tests that both create agents and subscribe to the same global
/// <see cref="ActivitySource"/> name will not see each other's activities in their captures.
/// </summary>
/// <remarks>
/// <para>
/// The .NET OpenTelemetry collection model is fundamentally source-based at the listener level
/// — every <see cref="ActivityListener"/> or <see cref="OpenTelemetry.Trace.TracerProvider"/>
/// subscribed to a given source name receives every activity created on that source, regardless
/// of which test or agent created it. Filtering must therefore happen on the receiving side.
/// </para>
/// <para>
/// This helper attaches a raw <see cref="ActivityListener"/> (not via OpenTelemetry SDK) and
/// applies an ownership predicate in its <see cref="ActivityListener.ActivityStopped"/> callback,
/// keeping the test's captured activities isolated from any other test running concurrently.
/// </para>
/// </remarks>
internal sealed class OwnerScopedActivityCapture : IDisposable
{
    private readonly List<Activity> _activities = new();
    private readonly object _lock = new();
    private readonly ActivityListener _listener;
    private readonly ChatClientAgent _ownerAgent;

    /// <summary>
    /// Creates a new capture scoped to the activities owned by <paramref name="ownerAgent"/>.
    /// </summary>
    /// <param name="ownerAgent">
    /// The <see cref="ChatClientAgent"/> whose own-pipeline activities should be captured. The
    /// per-instance marker set by either the bare self-wrap path or by
    /// <c>OpenTelemetryAgent.UpdateCurrentActivity</c> when this agent is the inner
    /// agent of an explicit decorator is matched via
    /// <see cref="object.ReferenceEquals(object?, object?)"/>.
    /// </param>
    public OwnerScopedActivityCapture(ChatClientAgent ownerAgent)
    {
        this._ownerAgent = ownerAgent;
        this._listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OpenTelemetryConsts.DefaultSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = this.OnActivityStopped,
        };
        ActivitySource.AddActivityListener(this._listener);
    }

    /// <summary>Gets the activities captured for this owner agent.</summary>
    public IReadOnlyList<Activity> Activities
    {
        get
        {
            lock (this._lock)
            {
                return this._activities.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose() => this._listener.Dispose();

    private void OnActivityStopped(Activity activity)
    {
        for (var current = activity; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current.GetCustomProperty(OpenTelemetryConsts.OwnedInvokeAgentScopeMarker), this._ownerAgent))
            {
                lock (this._lock)
                {
                    this._activities.Add(activity);
                }

                return;
            }
        }
    }
}
