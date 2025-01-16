// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// An orchestration request for processing a paged activity page-by-page.
/// </summary>
/// <typeparam name="TRequest">Self-referencing type for ContinueAsNew behavior.</typeparam>
/// <typeparam name="TPage">The type held by each page.</typeparam>
/// <typeparam name="TOutput">The output of the orchestration.</typeparam>
public abstract class PagedOrchestrationRequest<TRequest, TPage, TOutput> : IOrchestrationRequest<TOutput>
    where TRequest : PagedOrchestrationRequest<TRequest, TPage, TOutput>
    where TPage : notnull
{
    /// <summary>
    /// Gets the <see cref="IPagedActivityRequest{TPage}"/> to fetch individual pages from.
    /// </summary>
    /// <returns>The request that will be used to fetch individual pages.</returns>
    public abstract IPagedActivityRequest<TPage> GetPagedActivityRequest();

    /// <summary>
    /// Gets the next <see cref="PagedOrchestrationRequest{TRequest, TPage, TOutput}"/> to run. This is called for each <see cref="Page{TPage}"/>.
    /// <paramref name="current"/> is passed through from the previous page's output, allowing for output aggregation.
    /// </summary>
    /// <param name="current">The output from the current iteration.</param>
    /// <param name="continuationToken">The continuation token for the next page.</param>
    /// <returns>The next request to use in a "ContinueAsNew" call.</returns>
    public abstract TRequest GetNextRequest(TOutput current, string? continuationToken);

    /// <inheritdoc/>
    public abstract TaskName GetTaskName();
}

/// <summary>
/// An orchestration base to efficiently processes a <see cref="IPagedActivityRequest{TOutput}"/>.
/// </summary>
/// <typeparam name="TRequest">The <see cref="PagedOrchestrationRequest{TRequest, TPage, TOutput}"/> for each page iteration.</typeparam>
/// <typeparam name="TPage">The type held in each <see cref="Page{TPage}"/>.</typeparam>
/// <typeparam name="TOutput">The output type of this orchestration.</typeparam>
public abstract class PagedTaskOrchestration<TRequest, TPage, TOutput> : TaskOrchestrator<TRequest, TOutput>
    where TRequest : PagedOrchestrationRequest<TRequest, TPage, TOutput>
    where TPage : notnull
{
    /// <inheritdoc />
    public override async Task<TOutput> RunAsync(TaskOrchestrationContext context, TRequest input)
    {
        IPagedActivityRequest<TPage> pages = input.GetPagedActivityRequest();
        IAsyncEnumerator<Page<TPage>> enumerator = context.RunAsync(pages).AsPages().GetAsyncEnumerator();

        if (await enumerator.MoveNextAsync())
        {
            TOutput output = await this.RunAsync(context, input, enumerator.Current);
            await enumerator.DisposeAsync();
            if (enumerator.Current.ContinuationToken is { } ct)
            {
                context.ContinueAsNew(input.GetNextRequest(output, ct));
                return default!; // will not actually be used.
            }

            return output;
        }

        return await this.RunAsync(context, input, null);
    }

    /// <summary>
    /// Processes this page of data.
    /// </summary>
    /// <param name="context">The orchestration context.</param>
    /// <param name="input">The input to the orchestration.</param>
    /// <param name="page">The page to process. <c>null</c> if the activity yielded no data.</param>
    /// <returns>
    /// The ongoing output of this task. Will be passed to
    /// <see cref="PagedOrchestrationRequest{TRequest, TPage, TOutput}.GetNextRequest(TOutput, string?)"/>.
    /// </returns>
    protected abstract ValueTask<TOutput> RunAsync(TaskOrchestrationContext context, TRequest input, Page<TPage>? page);

    /// <summary>
    /// Base class for easily implementing a <see cref="PagedOrchestrationRequest{TRequest, TPage, TOutput}"/>.
    /// </summary>
    public abstract class RequestBase : PagedOrchestrationRequest<TRequest, TPage, TOutput>
    {
    }
}
