// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureStorage;

record SubExecutionStarted(DateTimeOffset Timestamp, TaskName Name, string? Input, int ScheduledId)
    : ExecutionStarted(Timestamp, Input);
