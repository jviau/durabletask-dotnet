// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Options for <see cref="OrchestrationRunner"/>.
/// </summary>
class OrchestrationRunnerOptions : WorkItemRunnerOptions
{
    /// <summary>
    /// Gets or sets the maximum timer interval for the
    /// <see cref="TaskOrchestrationContext.CreateTimer(TimeSpan, CancellationToken)"/> method.
    /// </summary>
    public TimeSpan MaximumTimerInterval { get; set; } = TimeSpan.FromDays(3);
}
