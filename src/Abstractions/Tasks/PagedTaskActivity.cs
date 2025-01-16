// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// A request representing a page.
/// </summary>
/// <typeparam name="TOutput">The type held by each page.</typeparam>
public interface IPagedActivityRequest<TOutput>
    where TOutput : notnull
{
    /// <summary>
    /// Create a new request with a continuation token.
    /// </summary>
    /// <param name="continuationToken">The continuation token.</param>
    /// <param name="pageSizeHint">The hint for the page size.</param>
    /// <returns>A request with the continuation token.</returns>
    IActivityRequest<Page<TOutput>> GetNextRequest(string? continuationToken, int? pageSizeHint);
}

/// <summary>
/// Extensions for <see cref="IPagedActivityRequest{TOutput}"/> on <see cref="TaskOrchestrationContext"/>.
/// </summary>
public static class PagedRequestExtensions
{
    /// <summary>
    /// Runs the paged activity described by <paramref name="request"/>, returning an <see cref="AsyncPageable{TOutput}"/>.
    /// </summary>
    /// <typeparam name="TOutput">The type held by each page result.</typeparam>
    /// <param name="context">The context used to run the activity.</param>
    /// <param name="request">The paged activity request.</param>
    /// <param name="options">The task options.</param>
    /// <returns>An <see cref="AsyncPageable{TOutput}"/>, which supports <see cref="IAsyncEnumerable{TOutput}"/> enumeration.</returns>
    public static AsyncPageable<TOutput> RunAsync<TOutput>(
        this TaskOrchestrationContext context, IPagedActivityRequest<TOutput> request, TaskOptions? options = null)
        where TOutput : notnull
    {
        return new TaskActivityAsyncPageable<TOutput>(context, request, options);
    }

    class TaskActivityAsyncPageable<TOutput>(TaskOrchestrationContext context, IPagedActivityRequest<TOutput> request, TaskOptions? options)
        : AsyncPageable<TOutput>
        where TOutput : notnull
    {
        public override async IAsyncEnumerable<Page<TOutput>> AsPages(string? continuationToken = null, int? pageSizeHint = null)
        {
            IActivityRequest<Page<TOutput>> next = request.GetNextRequest(continuationToken, pageSizeHint);
            Page<TOutput> page = await context.RunAsync(next, options);
            yield return page;

            while (page.ContinuationToken is { } ct)
            {
                next = request.GetNextRequest(ct, pageSizeHint);
                page = await context.RunAsync(next, options);
                yield return page;
            }
        }
    }
}
