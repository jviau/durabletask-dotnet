// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Options for a <see cref="IWorkItemRunner"/>.
/// </summary>
public class WorkItemRunnerOptions
{
    /// <summary>
    /// Gets or sets the data converter to use.
    /// </summary>
    public DataConverter DataConverter { get; set; } = null!;

    /// <summary>
    /// Gets or sets the durable factory to use.
    /// </summary>
    public IDurableTaskFactory Factory { get; set; } = null!;
}
