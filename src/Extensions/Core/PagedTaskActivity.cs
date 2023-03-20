// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

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
        this TaskOrchestrationContext context, PagedActivityRequest<TValue> request, TaskOptions? options = null)
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

        PagedActivityRequest<TValue> request;

        public TaskActivityAsyncPageable(
            TaskOrchestrationContext context, PagedActivityRequest<TValue> request, TaskOptions? options = null)
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
                this.request = this.request.GetNextRequest(continuationToken, pageSizeHint);
                Page<TValue> page = await this.context.RunAsync(this.request.GetPage(), this.options);
                yield return page;
                continuationToken = page.ContinuationToken;
            }
            while (continuationToken is not null);
        }
    }
}

/// <summary>
/// Contract for a paged activity request.
/// </summary>
/// <typeparam name="TValue">The type held by each page.</typeparam>
public abstract class PagedActivityRequest<TValue> : IActivityRequest<Page<TValue>>
    where TValue : notnull
{
    /// <summary>
    /// Gets the request for the next page.
    /// </summary>
    /// <param name="continuationToken">The optional continuation token.</param>
    /// <param name="pageSizeHint">The optional page size hint.</param>
    /// <returns>The next page request.</returns>
    public abstract PagedActivityRequest<TValue> GetNextRequest(
        string? continuationToken = null, int? pageSizeHint = null);

    /// <inheritdoc/>
    public abstract TaskName GetTaskName();

    /// <summary>
    /// Gets a request for the current page.
    /// </summary>
    /// <returns>A request for getting the current page.</returns>
    public virtual IActivityRequest<Page<TValue>> GetPage() => this;
}
