// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// The envelope for running an orchestration.
/// </summary>
readonly struct OrchestrationEnvelope
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationEnvelope"/> struct.
    /// </summary>
    /// <param name="id">The ID of the orchestration.</param>
    /// <param name="name">The name of the orchestration.</param>
    /// <param name="parent">The parent of the orchestration.</param>
    public OrchestrationEnvelope(string id, TaskName name, ParentOrchestrationInstance? parent)
    {
        this.Id = Check.NotNullOrEmpty(id);
        this.Name = name;
        this.Parent = parent;
        this.ScheduledId = null;
    }

    /// <summary>
    /// Gets the ID of the orchestration.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the name of the orchestration.
    /// </summary>
    public TaskName Name { get; }

    /// <summary>
    /// Gets the scheduled ID for this orchestration.
    /// </summary>
    public int? ScheduledId { get; init; }

    /// <summary>
    /// Gets the parent orchestration instance.
    /// </summary>
    public ParentOrchestrationInstance? Parent { get; }
}

/// <summary>
/// <see cref="OrchestrationWorkItem"/> provided by Azure Storage.
/// </summary>
partial class AzureStorageOrchestrationWorkItem : OrchestrationWorkItem
{
    readonly StorageOrchestrationChannel channel;
    readonly IOrchestrationSession session;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureStorageOrchestrationWorkItem"/> class.
    /// </summary>
    /// <param name="envelope">The envelope for this orchestration.</param>
    /// <param name="session">The channel that represents this session.</param>
    /// <param name="logger">The logger.</param>
    public AzureStorageOrchestrationWorkItem(
        OrchestrationEnvelope envelope,
        IOrchestrationSession session,
        ILogger<AzureStorageOrchestrationWorkItem> logger)
        : base(envelope.Id, envelope.Name)
    {
        this.Parent = envelope.Parent;
        this.session = Check.NotNull(session);
        this.channel = new(session, logger);
    }

    /// <inheritdoc/>
    public override ParentOrchestrationInstance? Parent { get; }

    /// <inheritdoc/>
    public override string? CustomStatus
    {
        get => this.channel.CustomStatus;
        set => this.channel.CustomStatus = value;
    }

    /// <inheritdoc/>
    public override bool IsReplaying => this.channel.IsReplaying;

    /// <inheritdoc/>
    public override Channel<OrchestrationMessage> Channel => this.channel;

    /// <summary>
    /// Gets a value indicating whether this is the first run of this work item or not.
    /// </summary>
    public bool FirstRun { get; init; }

    /// <inheritdoc/>
    public override ValueTask InitializeAsync(CancellationToken cancellation = default)
    {
        if (this.FirstRun)
        {
            // Transition into a "Running" state.
            return new(this.session.UpdateStateAsync(null));
        }

        return default;
    }

    /// <inheritdoc/>
    public override async Task ReleaseAsync(CancellationToken cancellation = default)
    {
        await this.channel.FlushAsync(cancellation);
        await this.session.ReleaseAsync();
    }
}
