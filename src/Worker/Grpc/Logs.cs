﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Grpc
{
    /// <summary>
    /// Log messages.
    /// </summary>
    /// <remarks>
    /// NOTE: Trying to make logs consistent with https://github.com/Azure/durabletask/blob/main/src/DurableTask.Core/Logging/LogEvents.cs.
    /// </remarks>
    static partial class Logs
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Durable Task gRPC worker starting.")]
        public static partial void StartingTaskHubWorker(this ILogger logger);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Durable Task gRPC worker has disconnected from gRPC server.")]
        public static partial void SidecarDisconnected(this ILogger logger);

        [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "The gRPC server for Durable Task gRPC worker is unavailable. Will continue retrying.")]
        public static partial void SidecarUnavailable(this ILogger logger);

        [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Sidecar work-item streaming connection established.")]
        public static partial void EstablishedWorkItemConnection(this ILogger logger);

        [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "{instanceId}: Received request to run orchestrator '{name}' with {oldEventCount} replay and {newEventCount} new history events.")]
        public static partial void ReceivedOrchestratorRequest(this ILogger logger, string name, string instanceId, int oldEventCount, int newEventCount);

        [LoggerMessage(EventId = 11, Level = LogLevel.Debug, Message = "{instanceId}: Sending {count} action(s) [{actionsList}] for '{name}' orchestrator.")]
        public static partial void SendingOrchestratorResponse(this ILogger logger, string name, string instanceId, int count, string actionsList);

        [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "{instanceId}: '{name}' orchestrator failed with an unhandled exception: {details}.")]
        public static partial void OrchestratorFailed(this ILogger logger, string name, string instanceId, string details);

        [LoggerMessage(EventId = 13, Level = LogLevel.Debug, Message = "{instanceId}: Received request to run activity '{name}#{taskId}' with {sizeInBytes} bytes of input data.")]
        public static partial void ReceivedActivityRequest(this ILogger logger, string name, int taskId, string instanceId, int sizeInBytes);

        [LoggerMessage(EventId = 14, Level = LogLevel.Debug, Message = "{instanceId}: Sending {successOrFailure} response for '{name}#{taskId}' activity with {sizeInBytes} bytes of output data.")]
        public static partial void SendingActivityResponse(this ILogger logger, string successOrFailure, string name, int taskId, string instanceId, int sizeInBytes);

        [LoggerMessage(EventId = 20, Level = LogLevel.Error, Message = "Unexpected error in handling of instance ID '{instanceId}'.")]
        public static partial void UnexpectedError(this ILogger logger, Exception ex, string instanceId);

        [LoggerMessage(EventId = 21, Level = LogLevel.Warning, Message = "Received and dropped an unknown '{type}' work-item from the sidecar.")]
        public static partial void UnexpectedWorkItemType(this ILogger logger, string type);

        [LoggerMessage(EventId = 55, Level = LogLevel.Information, Message = "{instanceId}: Evaluating custom retry handler for failed '{name}' task. Attempt = {attempt}.")]
        public static partial void RetryingTask(this ILogger logger, string instanceId, string name, int attempt);
    }
}
