// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Thrown when a scheduled work item is missing and unable to run.
/// </summary>
public class TaskMissingException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TaskMissingException"/> class.
    /// </summary>
    /// <param name="name">The name of the missing task.</param>
    /// <param name="type">The type of the task that is missing.</param>
    public TaskMissingException(TaskName name, Type type)
        : base($"Task '{name}' of type '{type}' is missing and cannot be ran.")
    {
        this.Name = Check.NotDefault(name);
        this.TaskType = Check.NotNull(type);
    }

    /// <summary>
    /// Gets the name of the missing task.
    /// </summary>
    public TaskName Name { get; }

    /// <summary>
    /// Gets the type of the missing task.
    /// </summary>
    public Type TaskType { get; }
}
