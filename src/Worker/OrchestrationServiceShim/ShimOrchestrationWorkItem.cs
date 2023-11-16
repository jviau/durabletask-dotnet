// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using DurableTask.Core;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim;

/// <summary>
/// <see cref="OrchestrationWorkItem"/> backed by a <see cref="TaskOrchestrationWorkItem"/>.
/// </summary>
class ShimOrchestrationWorkItem : OrchestrationWorkItem, IDisposable
{
    readonly CancellationTokenSource cts = new();
    readonly IOrchestrationService service;
    readonly TaskOrchestrationWorkItem inner;
    readonly ShimOrchestrationChannel channel;
    readonly Task lockRenewal;

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

        if (inner.LockedUntilUtc < DateTime.MaxValue)
        {
            this.lockRenewal = this.RenewLockAsync(this.cts.Token);
        }
        else
        {
            this.lockRenewal = Task.CompletedTask;
        }
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
    public void Dispose()
    {
        this.cts.Dispose();
    }

    /// <inheritdoc/>
    public override async Task ReleaseAsync(CancellationToken cancellation = default)
    {
        try
        {
            if (!this.cts.IsCancellationRequested)
            {
                this.cts.Cancel();
            }

            await this.lockRenewal;
            cancellation.ThrowIfCancellationRequested();
            await this.channel.CompleteExecutionAsync();
        }
        finally
        {
            await this.service.ReleaseTaskOrchestrationWorkItemAsync(this.inner);
        }
    }

    async Task RenewLockAsync(CancellationToken cancellation)
    {
        TimeSpan minRenewalInterval = TimeSpan.FromSeconds(5); // prevents excessive retries if clocks are off
        TimeSpan maxRenewalInterval = TimeSpan.FromSeconds(30);
        while (!cancellation.IsCancellationRequested)
        {
            TimeSpan delay = this.inner.LockedUntilUtc - DateTime.UtcNow - TimeSpan.FromSeconds(30);
            if (delay < minRenewalInterval)
            {
                delay = minRenewalInterval;
            }
            else if (delay > maxRenewalInterval)
            {
                delay = maxRenewalInterval;
            }

            try
            {
                await Task.Delay(delay, cancellation);
                await this.service.RenewTaskOrchestrationWorkItemLockAsync(this.inner);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
