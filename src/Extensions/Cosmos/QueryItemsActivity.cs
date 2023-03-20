// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;

namespace Microsoft.DurableTask.Extensions.Cosmos;

/// <summary>
/// A request to perform a query on CosmosDB.
/// </summary>
/// <typeparam name="TValue">The type returned by the query.</typeparam>
public sealed class QueryItemsActivityRequest<TValue> : PagedActivityRequest<TValue>
    where TValue : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueryItemsActivityRequest{TValue}"/> class.
    /// </summary>
    /// <param name="database">The Cosmos database name.</param>
    /// <param name="container">The Cosmos container name.</param>
    /// <param name="query">The Cosmos query definition.</param>
    public QueryItemsActivityRequest(string database, string container, QueryDefinition query)
    {
        this.Database = Check.NotNullOrEmpty(database);
        this.Container = Check.NotNullOrEmpty(container);
        this.Query = Check.NotNull(query);
    }

    /// <summary>
    /// Gets the database name for the query.
    /// </summary>
    public string Database { get; }

    /// <summary>
    /// Gets the container name for the query.
    /// </summary>
    public string Container { get; }

    /// <summary>
    /// Gets the query definition to execute.
    /// </summary>
    public QueryDefinition Query { get; }

    /// <summary>
    /// Gets or sets the continuation token.
    /// </summary>
    public string? ContinuationToken { get; set; }

    /// <summary>
    /// Gets or sets the page size.
    /// </summary>
    public int? PageSize { get; set; }

    /// <inheritdoc/>
    public override TaskName GetTaskName() => "QueryItemsActivity";

    /// <inheritdoc />
    public override PagedActivityRequest<TValue> GetNextRequest(
        string? continuationToken = null, int? pageSizeHint = null)
    {
        return new QueryItemsActivityRequest<TValue>(this.Database, this.Container, this.Query)
        {
            ContinuationToken = continuationToken,
            PageSize = pageSizeHint ?? this.PageSize,
        };
    }
}

/// <summary>
/// Performs a query on CosmosDB.
/// </summary>
/// <typeparam name="TValue">The type returned by the query.</typeparam>
public sealed class QueryItemsActivity<TValue> : TaskActivity<QueryItemsActivityRequest<TValue>, Page<TValue>>
    where TValue : notnull
{
    readonly CosmosClient client;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryItemsActivity{TValue}"/> class.
    /// </summary>
    /// <param name="client">The CosmosDB client to use.</param>
    public QueryItemsActivity(CosmosClient client)
    {
        this.client = Check.NotNull(client);
    }

    /// <inheritdoc/>
    public override async Task<Page<TValue>> RunAsync(
        TaskActivityContext context, QueryItemsActivityRequest<TValue> input)
    {
        Container container = this.client.GetContainer(input.Database, input.Container);
        QueryRequestOptions options = new()
        {
            MaxItemCount = input.PageSize,
        };

        using FeedIterator<TValue> iterator = container.GetItemQueryIterator<TValue>(
            input.Query, input.ContinuationToken, options);
        FeedResponse<TValue> response = await iterator.ReadNextAsync();
        return new Page<TValue>(response.ToList(), response.ContinuationToken);
    }
}
