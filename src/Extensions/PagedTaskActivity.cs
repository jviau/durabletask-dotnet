// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Extensions;

/// <summary>
/// Contract for a paged activity request.
/// </summary>
/// <typeparam name="TValue">The type held by the page.</typeparam>
public interface IPagedActivityRequest<TValue> : IActivityRequest<Page<TValue>>
    where TValue : notnull
{
    /// <summary>
    /// Gets the request for the next page.
    /// </summary>
    /// <param name="continuationToken">The optional continuation token.</param>
    /// <param name="pageSizeHint">The optional page size hint.</param>
    /// <returns>The next page request.</returns>
    IPagedActivityRequest<TValue> NextPageRequest(string? continuationToken = null, int? pageSizeHint = null);
}

/// <summary>
/// Extensions for paged task activities.
/// </summary>
public static class PagedTaskOrchestrationContextExtensions
{
    /// <summary>
    /// Runs the activity described by <paramref name="request" />, paging over results until no continuation token
    /// is left.
    /// </summary>
    /// <param name="context">The task orchestration context.</param>
    /// <param name="request">The request which provides successive page requests.</param>
    /// <param name="options">
    /// The task options for this invocation. Note: these options will apply to each individual page.
    /// </param>
    /// <typeparam name="TValue">The type held by each page.</typeparam>
    /// <returns>An <see cref="AsyncPageable{TValue}" /> which can be used to enumerate over the results.</returns>
    /// <seealso cref="AsyncPageable{TValue}" />.
    public static AsyncPageable<TValue> RunAsync<TValue>(
        this TaskOrchestrationContext context, IPagedActivityRequest<TValue> request, TaskOptions? options = null)
        where TValue : notnull
    {
        Check.NotNull(context);
        Check.NotNull(request);
        return new TaskActivityAsyncPageable<TValue>(context, request, options);
    }

    sealed class TaskActivityAsyncPageable<TValue> : AsyncPageable<TValue>
        where TValue : notnull
    {
        readonly TaskOrchestrationContext context;
        readonly TaskOptions? options;

        IPagedActivityRequest<TValue> request;

        public TaskActivityAsyncPageable(
            TaskOrchestrationContext context,
            IPagedActivityRequest<TValue> request,
            TaskOptions? options = null)
        {
            this.context = context;
            this.request = request;
            this.options = options;
        }

        public async override IAsyncEnumerable<Page<TValue>> AsPages(
            string? continuationToken = null, int? pageSizeHint = null)
        {
            do
            {
                this.request = this.request.NextPageRequest(continuationToken, pageSizeHint);
                IActivityRequest<Page<TValue>> downCast = this.request; // so we call the other overload.
                Page<TValue> page = await this.context.RunAsync(downCast, this.options);
                yield return page;
                continuationToken = page.ContinuationToken;
            }
            while (continuationToken is not null);
        }
    }
}
