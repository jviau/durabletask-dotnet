// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Azure;

namespace Microsoft.DurableTask.Extensions.CosmosDb;

/// <summary>
/// Extensions for <see cref="DurableTaskRegistry"/>.
/// </summary>
public static class DurableTaskRegistryExtensions
{
    /// <summary>
    /// Registers a type as queryable against a CosmosDb <see cref="Container"/>.
    /// </summary>
    /// <typeparam name="TOutput">The type to register.</typeparam>
    /// <param name="registry">The durable task registry.</param>
    /// <returns>The durable task registry, for call chaining.</returns>
    public static DurableTaskRegistry AddQueryContainerActivity<TOutput>(this DurableTaskRegistry registry)
        where TOutput : notnull
    {
        QueryContainerActivity<TOutput>.Register(registry);
        return registry;
    }
}

/// <summary>
/// A request to query a CosmosDb <see cref="Container"/>.
/// </summary>
/// <param name="container">The name of the container to query. Retrieved from <see cref="IAzureClientFactory{Container}"/>.</param>
/// <typeparam name="TOutput">The item type yielded by the query.</typeparam>
/// <param name="query">The query to perform.</param>
public class QueryContainerActivity<TOutput>(QueryDefinition query, string? container = null)
    : IPagedActivityRequest<TOutput>
    where TOutput : notnull
{
    static readonly TaskName TaskName = nameof(QueryContainerActivity<TOutput>);

    /// <summary>
    /// Gets the query to run against the <see cref="Container"/>.
    /// </summary>
    public QueryDefinition Query { get; } = Check.NotNull(query);

    /// <summary>
    /// Gets the name of the <see cref="Container"/> to use, retrieved from <see cref="IAzureClientFactory{Container}"/>.
    /// </summary>
    public string? ContainerName { get; } = container;

    /// <summary>
    /// Gets or sets the options for this query.
    /// </summary>
    public QueryRequestOptions? Options { get; set; }

    /// <summary>
    /// Gets or sets the continuation token to use.
    /// </summary>
    public string? ContinuationToken { get; set; }

    /// <summary>
    /// Registers this activity to the specified <see cref="DurableTaskRegistry"/>.
    /// </summary>
    /// <param name="registry">The registry to use.</param>
    public static void Register(DurableTaskRegistry registry)
    {
        Check.NotNull(registry);
        registry.AddActivity<Handler>(TaskName);
    }

    /// <inheritdoc/>
    public IActivityRequest<Page<TOutput>> GetNextRequest(string? continuationToken, int? pageSizeHint)
    {
        this.ContinuationToken = continuationToken;

        if (pageSizeHint is int size)
        {
            this.Options ??= new QueryRequestOptions();
            this.Options.MaxItemCount = size;
        }

        return ActivityRequest.Create<Page<TOutput>>(TaskName, this);
    }

    class Handler(IAzureClientFactory<Container> containers) : TaskActivity<QueryContainerActivity<TOutput>, Page<TOutput>>
    {
        public override async Task<Page<TOutput>> RunAsync(TaskActivityContext context, QueryContainerActivity<TOutput> input)
        {
            Check.NotNull(context);
            Check.NotNull(input);

            Container container = containers.CreateClient(input.ContainerName);
            FeedIterator<TOutput> iterator = container.GetItemQueryIterator<TOutput>(input.Query, input.ContinuationToken, input.Options);
            FeedResponse<TOutput> response = await iterator.ReadNextAsync();

            return new Page<TOutput>([.. response], response.ContinuationToken);
        }
    }
}
