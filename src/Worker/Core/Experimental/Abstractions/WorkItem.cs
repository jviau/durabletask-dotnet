// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Represents a task work item.
/// </summary>
public abstract class WorkItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkItem"/> class.
    /// </summary>
    /// <param name="id">The ID of this work item.</param>
    /// <param name="name">The name of the work item.</param>
    protected WorkItem(string id, TaskName name)
    {
        this.Id = Check.NotNullOrEmpty(id);
        this.Name = Check.NotDefault(name);
        this.Metadata = Dictionary.ReadOnly<string, string>.Empty;
    }

    /// <summary>
    /// Gets the work item ID.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the task name.
    /// </summary>
    public TaskName Name { get; }

    /// <summary>
    /// Gets the metadata associated with this work item.
    /// </summary>
    public virtual IReadOnlyDictionary<string, string> Metadata { get; }
}
