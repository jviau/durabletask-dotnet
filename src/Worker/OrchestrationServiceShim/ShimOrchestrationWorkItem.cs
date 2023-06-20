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
        this.channel = new ShimOrchestrationChannel(inner.OrchestrationRuntimeState);
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
            if (this.channel.Abort)
            {
                await this.service.AbandonTaskOrchestrationWorkItemAsync(this.inner);
            }
            else
            {
                OrchestrationRuntimeState state = this.channel.CompleteExecution();
                this.inner.OrchestrationRuntimeState = state;
                await this.service.CompleteTaskOrchestrationWorkItemAsync(
                    this.inner,
                    state,
                    this.channel.ActivityMessages,
                    this.channel.OrchestratorMessages,
                    this.channel.TimerMessages,
                    continuedAsNewMessage: null,
                    BuildState(state));
            }
        }
        finally
        {
            await this.service.ReleaseTaskOrchestrationWorkItemAsync(this.inner);
        }
    }

    static OrchestrationState BuildState(OrchestrationRuntimeState runtimeState)
    {
        return new()
        {
            OrchestrationInstance = runtimeState.OrchestrationInstance,
            ParentInstance = runtimeState.ParentInstance,
            Name = runtimeState.Name,
            Version = runtimeState.Version,
            Status = runtimeState.Status,
            Tags = runtimeState.Tags,
            OrchestrationStatus = runtimeState.OrchestrationStatus,
            CreatedTime = runtimeState.CreatedTime,
            CompletedTime = runtimeState.CompletedTime,
            LastUpdatedTime = DateTime.UtcNow,
            Size = runtimeState.Size,
            CompressedSize = runtimeState.CompressedSize,
            Input = runtimeState.Input,
            Output = runtimeState.Output,
            ScheduledStartTime = runtimeState.ExecutionStartedEvent?.ScheduledStartTime,
            FailureDetails = runtimeState.FailureDetails,
        };
    }
}
