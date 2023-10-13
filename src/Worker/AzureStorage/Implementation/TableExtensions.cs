// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Azure.Data.Tables;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Extensions for Azure.Data.Tables.
/// </summary>
static class TableExtensions
{
    /// <summary>
    /// Tries to add an entity to a table.
    /// </summary>
    /// <typeparam name="T">The type of entity to add.</typeparam>
    /// <param name="table">The table to add to.</param>
    /// <param name="entity">The entity to add.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>True if table added, false otherwise.</returns>
    public static async Task<bool> TryAddEntityAsync<T>(
        this TableClient table, T entity, CancellationToken cancellation = default)
        where T : ITableEntity
    {
        Check.NotNull(table);

        try
        {
            await table.AddEntityAsync(entity, cancellation);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false;
        }
    }
}
