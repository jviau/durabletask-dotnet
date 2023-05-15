// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;

namespace Microsoft.DurableTask;

/// <summary>
/// Task retry options. Can provide either a <see cref="RetryPolicy" /> for declarative retry or a
/// <see cref="AsyncRetryHandler" /> for imperative retry control.
/// </summary>
public sealed class TaskRetryOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TaskRetryOptions"/> class.
    /// </summary>
    /// <param name="policy">The retry policy to use.</param>
    public TaskRetryOptions(RetryPolicy policy)
    {
        this.Policy = Check.NotNull(policy);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskRetryOptions"/> class.
    /// </summary>
    /// <param name="handler">The retry handler to use.</param>
    public TaskRetryOptions(AsyncRetryHandler handler)
    {
        this.Handler = Check.NotNull(handler);
    }

    /// <summary>
    /// Gets the retry policy. <c>null</c> if <see cref="Handler" /> is set.
    /// </summary>
    public RetryPolicy? Policy { get; }

    /// <summary>
    /// Gets the retry handler. <c>null</c> if <see cref="Policy" /> is set.
    /// </summary>
    public AsyncRetryHandler? Handler { get; }

    /// <summary>
    /// Returns a new <see cref="TaskRetryOptions" /> from the provided <see cref="RetryPolicy" />.
    /// </summary>
    /// <param name="policy">The policy to convert from.</param>
    public static implicit operator TaskRetryOptions(RetryPolicy policy) => FromRetryPolicy(policy);

    /// <summary>
    /// Returns a new <see cref="TaskRetryOptions" /> from the provided <see cref="AsyncRetryHandler" />.
    /// </summary>
    /// <param name="handler">The handler to convert from.</param>
    public static implicit operator TaskRetryOptions(AsyncRetryHandler handler) => FromRetryHandler(handler);

    /// <summary>
    /// Returns a new <see cref="TaskRetryOptions" /> from the provided <see cref="RetryHandler" />.
    /// </summary>
    /// <param name="handler">The handler to convert from.</param>
    public static implicit operator TaskRetryOptions(RetryHandler handler) => FromRetryHandler(handler);

    /// <summary>
    /// Returns a new <see cref="TaskRetryOptions" /> from the provided <see cref="RetryPolicy" />.
    /// </summary>
    /// <param name="policy">The policy to convert from.</param>
    /// <returns>A <see cref="TaskRetryOptions" /> built from the policy.</returns>
    public static TaskRetryOptions FromRetryPolicy(RetryPolicy policy) => new(policy);

    /// <summary>
    /// Returns a new <see cref="TaskRetryOptions" /> from the provided <see cref="AsyncRetryHandler" />.
    /// </summary>
    /// <param name="handler">The handler to convert from.</param>
    /// <returns>A <see cref="TaskRetryOptions" /> built from the handler.</returns>
    public static TaskRetryOptions FromRetryHandler(AsyncRetryHandler handler) => new(handler);

    /// <summary>
    /// Returns a new <see cref="TaskRetryOptions" /> from the provided <see cref="RetryHandler" />.
    /// </summary>
    /// <param name="handler">The handler to convert from.</param>
    /// <returns>A <see cref="TaskRetryOptions" /> built from the handler.</returns>
    public static TaskRetryOptions FromRetryHandler(RetryHandler handler)
    {
        Check.NotNull(handler);
        return FromRetryHandler(context => Task.FromResult(handler.Invoke(context)));
    }

    /// <summary>
    /// use this retry policy to invoke an orchestration action.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="context">The orchestration context.</param>
    /// <param name="invoker">The call to retry.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>The result of <paramref name="invoker"/>.</returns>
    internal Task<T> InvokeAsync<T>(
        TaskOrchestrationContext context, Func<CancellationToken, Task<T>> invoker, CancellationToken cancellation)
    {
        return this switch
        {
            { Policy: { } p } => p.InvokeAsync(context, invoker, cancellation),
            { Handler: { } h } => InvokeHandlerAsync(h, context, invoker, cancellation),
            _ => throw new InvalidOperationException("No retry policy configured."), // unreachable
        };
    }

    static async Task<T> InvokeHandlerAsync<T>(
        AsyncRetryHandler handler,
        TaskOrchestrationContext context,
        Func<CancellationToken, Task<T>> invoker,
        CancellationToken cancellation)
    {
        DateTime start = context.CurrentUtcDateTime;
        int attempt = 0;

        while (true)
        {
            try
            {
                return await invoker.Invoke(cancellation);
            }
            catch (Exception ex)
            {
                TaskFailureDetails details = TaskFailureDetails.FromException(ex);
                if (details.IsCausedBy<TaskMissingException>())
                {
                    throw;
                }

                attempt++;
                RetryContext retryContext = new(
                    context,
                    attempt,
                    details,
                    context.CurrentUtcDateTime.Subtract(start),
                    default);

                if (!await handler(retryContext))
                {
                    throw;
                }

                if (attempt == int.MaxValue)
                {
                    // Integer overflow check.
                    throw;
                }
            }
        }
    }
}
