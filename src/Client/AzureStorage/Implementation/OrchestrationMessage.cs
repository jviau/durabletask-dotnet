// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.Client.AzureStorage.Implementation;

/// <summary>
/// Represents a piece of the saved state of an <see cref="ITaskOrchestrator" />.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
[JsonDerivedType(typeof(ExecutionStarted), nameof(ExecutionStarted))]
abstract record OrchestrationMessage(int Id, DateTimeOffset Timestamp);

/// <summary>
/// <see cref="ITaskOrchestrator" /> execution started message.
/// </summary>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="Input">The serialized orchestration input.</param>
record ExecutionStarted(DateTimeOffset Timestamp, string? Input)
    : OrchestrationMessage(-1, Timestamp);
