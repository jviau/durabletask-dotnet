// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Azure storage queue helpers.
/// </summary>
static class QueueHelpers
{
    const int MaxBatchSize = 32;

    /// <summary>
    /// Gets the batch size for a queue receive call.
    /// </summary>
    /// <param name="max">The max messages to hold.</param>
    /// <param name="current">The current messages held.</param>
    /// <returns>The batch size, capped at 32.</returns>
    public static int GetBatchSize(int max, int current)
    {
        int size = max - current;
        return Math.Min(size, MaxBatchSize);
    }
}
