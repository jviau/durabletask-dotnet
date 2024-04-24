// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Storage channel for <see cref="OrchestrationMessage"/>.
/// </summary>
partial class StorageOrchestrationChannel : Channel<OrchestrationMessage>
{
    readonly IOrchestrationSession session;
    readonly ILogger logger;
    readonly Channel<Func<Task>> pendingActions = Channel.CreateUnbounded<Func<Task>>();

    string? customStatus;
    bool forceFlush;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageOrchestrationChannel"/> class.
    /// </summary>
    /// <param name="session">The orchestration session.</param>
    /// <param name="logger">The logger.</param>
    public StorageOrchestrationChannel(IOrchestrationSession session, ILogger logger)
    {
        this.session = Check.NotNull(session);
        this.logger = Check.NotNull(logger);
        this.Reader = new StorageReader(this);
        this.Writer = new StorageWriter(this);
    }

    /// <summary>
    /// Gets a value indicating whether this channel is replaying messages or not.
    /// </summary>
    public bool IsReplaying => ((StorageReader)this.Reader).IsReplaying;

    /// <summary>
    /// Gets or sets the custom status for this orchestration.
    /// </summary>
    public string? CustomStatus
    {
        get => this.customStatus;
        set
        {
            this.customStatus = value;
            if (!this.IsReplaying)
            {
                this.pendingActions.Writer.TryWrite(() => this.session.UpdateStateAsync(value));
                this.forceFlush = true;
            }
        }
    }

    bool FlushNeeded => this.forceFlush || this.pendingActions.Reader.Count > 10;

    /// <summary>
    /// Flushes this channel, performing any pending work as necessary.
    /// </summary>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when flush is complete.</returns>
    public async Task FlushAsync(CancellationToken cancellation = default)
    {
        // We only flush the current items in the pending actions. Don't wait for more.
        int count = 0;
        while (this.pendingActions.Reader.TryRead(out Func<Task>? action))
        {
            cancellation.ThrowIfCancellationRequested();
            await action();
            count++;
        }

        this.logger.LogDebug("Flushed {FlushCount} items", count);
        this.forceFlush = false;
    }
}
