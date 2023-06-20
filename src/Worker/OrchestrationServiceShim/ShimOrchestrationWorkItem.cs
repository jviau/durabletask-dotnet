// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using DurableTask.Core;
using DurableTask.Core.History;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim;

/// <summary>
/// <see cref="OrchestrationWorkItem"/> backed by a <see cref="TaskOrchestrationWorkItem"/>.
/// </summary>
class ShimOrchestrationWorkItem : OrchestrationWorkItem
{
    readonly IOrchestrationService service;
    readonly TaskOrchestrationWorkItem inner;
    readonly ShimOrchestrationChannel channel;

    ParentOrchestrationInstance? parent;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShimOrchestrationWorkItem"/> class.
    /// </summary>
    /// <param name="service">The orchestration service.</param>
    /// <param name="inner">The work item.</param>
    public ShimOrchestrationWorkItem(
        IOrchestrationService service, TaskOrchestrationWorkItem inner)
        : base(Check.NotNull(inner).InstanceId, inner.GetName())
    {
        this.service = Check.NotNull(service);
        this.inner = Check.NotNull(inner);
        this.channel = new ShimOrchestrationChannel(service, inner);
    }

    /// <inheritdoc/>
    public override ParentOrchestrationInstance? Parent => this.parent ??= this.inner.GetParent();

    /// <inheritdoc/>
    public override string? CustomStatus
    {
        get => this.inner.OrchestrationRuntimeState.Status;
        set => this.inner.OrchestrationRuntimeState.Status = value;
    }

    /// <inheritdoc/>
    public override bool IsReplaying => this.channel.IsReplaying;

    /// <inheritdoc/>
    public override Channel<OrchestrationMessage> Channel => this.channel;

    /// <inheritdoc/>
    public override async Task ReleaseAsync(CancellationToken cancellation = default)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            await this.channel.CompleteExecutionAsync();
        }
        finally
        {
            await this.service.ReleaseTaskOrchestrationWorkItemAsync(this.inner);
        }
    }
}
