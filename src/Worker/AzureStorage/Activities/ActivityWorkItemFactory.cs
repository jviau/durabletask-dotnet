// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Factory for activity work items.
/// </summary>
class ActivityWorkItemFactory
{
    readonly DurableStorageClientOptions options;
    readonly ILoggerFactory loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityWorkItemFactory"/> class.
    /// </summary>
    /// <param name="options">The client options.</param>
    /// <param name="loggerFactory">The logger factor.</param>
    public ActivityWorkItemFactory(DurableStorageClientOptions options, ILoggerFactory loggerFactory)
    {
        this.options = Check.NotNull(options);
        this.loggerFactory = Check.NotNull(loggerFactory);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityWorkItemFactory"/> class.
    /// </summary>
    protected ActivityWorkItemFactory()
    {
        // for mocking only.
        this.options = null!;
        this.loggerFactory = null!;
    }

    /// <summary>
    /// Creates a <see cref="ActivityWorkItem"/> for a given <see cref="WorkMessage"/>.
    /// </summary>
    /// <param name="work">The work message.</param>
    /// <returns>The activity work item.</returns>
    public virtual ActivityWorkItem Create(WorkMessage work)
    {
        Check.NotNull(work);
        if (work.Message is not TaskActivityScheduled)
        {
            throw new ArgumentException(
                $"Expected a {nameof(TaskActivityScheduled)}, received a '{work.Message?.GetType()}'.");
        }

        return new AzureStorageActivityWorkItem(
            work,
            this.options.ActivityQueue,
            this.options.GetQueue(work.Parent!.QueueName),
            this.loggerFactory.CreateLogger<AzureStorageActivityWorkItem>());
    }
}
