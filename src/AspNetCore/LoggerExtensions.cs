// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AspNetCore;

/// <summary>
/// Extensions for <see cref="ILogger"/>.
/// </summary>
static partial class LoggerExtensions
{
    [LoggerMessage(1, LogLevel.Information, "Orchestration with instance ID '{InstanceId}' not found.")]
    public static partial void OrchestrationNotFound(this ILogger logger, string instanceId);

    [LoggerMessage(2, LogLevel.Debug, "Operation location header set to '{Route}'.")]
    public static partial void OperationLocationHeaderSet(this ILogger logger, string route);

    [LoggerMessage(3, LogLevel.Error, "Cannot set operation location header. No route matches name: '{RouteName}',"
        + " parameter: '{ParameterName}'.")]
    public static partial void CannotFindRoute(this ILogger logger, string routeName, string parameterName);
}
