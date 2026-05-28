// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Default <see cref="IHostedTargetRunner"/> for <see cref="Workflow"/> targets.
/// </summary>
/// <remarks>
/// Skeletal in this draft: surfaces the workflow object via <see cref="HostedRunResult{TResult}"/>
/// without driving execution end-to-end. The intent is to fill in resume-token handling and
/// <c>RequestInfoEvent</c> projection in a follow-up commit once the channel surface (Invocations)
/// is in place to consume them.
/// </remarks>
public sealed class WorkflowRunner : IHostedTargetRunner
{
    private readonly Workflow _workflow;

    /// <summary>Initializes a new instance.</summary>
    public WorkflowRunner(Workflow workflow)
    {
        this._workflow = Throw.IfNull(workflow);
    }

    /// <summary>The wrapped workflow.</summary>
    public Workflow Workflow => this._workflow;

    /// <inheritdoc />
    public ValueTask<HostedRunResult> RunAsync(ChannelRequest request, CancellationToken cancellationToken) =>
        throw new NotImplementedException("WorkflowRunner is a draft placeholder; end-to-end wiring lands with the InvocationsChannel package.");

    /// <inheritdoc />
    public IAsyncEnumerable<HostedStreamItem> StreamAsync(ChannelRequest request, CancellationToken cancellationToken) =>
        throw new NotImplementedException("WorkflowRunner is a draft placeholder; end-to-end wiring lands with the InvocationsChannel package.");
}