// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.ExceptionServices;

namespace Microsoft.DurableTask;

/// <summary>
/// A declarative retry policy that can be configured for activity or sub-orchestration calls.
/// </summary>
public class RetryPolicy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RetryPolicy"/> class.
    /// </summary>
    /// <param name="maxNumberOfAttempts">The maximum number of task invocation attempts. Must be 1 or greater.</param>
    /// <param name="firstRetryInterval">The amount of time to delay between the first and second attempt.</param>
    /// <param name="backoffCoefficient">
    /// The exponential back-off coefficient used to determine the delay between subsequent retries. Must be 1.0 or greater.
    /// </param>
    /// <param name="maxRetryInterval">
    /// The maximum time to delay between attempts, regardless of<paramref name="backoffCoefficient"/>.
    /// </param>
    /// <param name="retryTimeout">The overall timeout for retries.</param>
    /// <remarks>
    /// The value <see cref="Timeout.InfiniteTimeSpan"/> can be used to specify an unlimited timeout for
    /// <paramref name="maxRetryInterval"/> or <paramref name="retryTimeout"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if any of the following are true:
    /// <list type="bullet">
    ///   <item>The value for <paramref name="maxNumberOfAttempts"/> is less than or equal to zero.</item>
    ///   <item>The value for <paramref name="firstRetryInterval"/> is less than or equal to <see cref="TimeSpan.Zero"/>.</item>
    ///   <item>The value for <paramref name="backoffCoefficient"/> is less than 1.0.</item>
    ///   <item>The value for <paramref name="maxRetryInterval"/> is less than <paramref name="firstRetryInterval"/>.</item>
    ///   <item>The value for <paramref name="retryTimeout"/> is less than <paramref name="firstRetryInterval"/>.</item>
    /// </list>
    /// </exception>
    public RetryPolicy(
        int maxNumberOfAttempts,
        TimeSpan firstRetryInterval,
        double backoffCoefficient = 1.0,
        TimeSpan? maxRetryInterval = null,
        TimeSpan? retryTimeout = null)
    {
        if (maxNumberOfAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName: nameof(maxNumberOfAttempts),
                actualValue: maxNumberOfAttempts,
                message: $"The value for {nameof(maxNumberOfAttempts)} must be greater than zero.");
        }

        if (firstRetryInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                paramName: nameof(firstRetryInterval),
                actualValue: firstRetryInterval,
                message: $"The value for {nameof(firstRetryInterval)} must be greater than zero.");
        }

        if (backoffCoefficient < 1.0)
        {
            throw new ArgumentOutOfRangeException(
                paramName: nameof(backoffCoefficient),
                actualValue: backoffCoefficient,
                message: $"The value for {nameof(backoffCoefficient)} must be greater or equal to 1.0.");
        }

        if (maxRetryInterval < firstRetryInterval && maxRetryInterval != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                paramName: nameof(maxRetryInterval),
                actualValue: maxRetryInterval,
                message: $"The value for {nameof(maxRetryInterval)} must be greater or equal to the value for {nameof(firstRetryInterval)}.");
        }

        if (retryTimeout < firstRetryInterval && retryTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                paramName: nameof(retryTimeout),
                actualValue: retryTimeout,
                message: $"The value for {nameof(retryTimeout)} must be greater or equal to the value for {nameof(firstRetryInterval)}.");
        }

        this.MaxNumberOfAttempts = maxNumberOfAttempts;
        this.FirstRetryInterval = firstRetryInterval;
        this.BackoffCoefficient = backoffCoefficient;
        this.MaxRetryInterval = maxRetryInterval ?? TimeSpan.FromHours(1);
        this.RetryTimeout = retryTimeout ?? Timeout.InfiniteTimeSpan;
    }

    /// <summary>
    /// Gets the max number of attempts for executing a given task.
    /// </summary>
    public int MaxNumberOfAttempts { get; }

    /// <summary>
    /// Gets the amount of time to delay between the first and second attempt.
    /// </summary>
    public TimeSpan FirstRetryInterval { get; }

    /// <summary>
    /// Gets the exponential back-off coefficient used to determine the delay between subsequent retries.
    /// </summary>
    /// <value>
    /// Defaults to 1.0 for no back-off.
    /// </value>
    public double BackoffCoefficient { get; }

    /// <summary>
    /// Gets the maximum time to delay between attempts.
    /// </summary>
    /// <value>
    /// Defaults to 1 hour.
    /// </value>
    public TimeSpan MaxRetryInterval { get; }

    /// <summary>
    /// Gets the overall timeout for retries. No further attempts will be made at executing a task after this retry
    /// timeout expires.
    /// </summary>
    /// <value>
    /// Defaults to <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </value>
    public TimeSpan RetryTimeout { get; }

    /// <summary>
    /// Gets or sets a Func to call on exception to determine if retries should proceed.
    /// </summary>
    public Func<Exception, Task<bool>>? HandleAsync { get; set; }

    /// <summary>
    /// use this retry policy to invoke an orchestration action.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="context">The orchestration context.</param>
    /// <param name="invoker">The call to retry.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>The result of <paramref name="invoker"/>.</returns>
    internal async Task<T> InvokeAsync<T>(
        TaskOrchestrationContext context,
        Func<CancellationToken, Task<T>> invoker,
        CancellationToken cancellation = default)
    {
        Check.NotNull(context);
        Check.NotNull(invoker);

        Exception? lastException = null;
        DateTimeOffset startTime = context.CurrentUtcDateTime;
        for (int attempt = 0; attempt < this.MaxNumberOfAttempts; attempt++)
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                return await invoker.Invoke(cancellation);
            }
            catch (TaskFailedException ex)
            {
                if (ex.FailureDetails.IsCausedBy<TaskMissingException>())
                {
                    throw;
                }

                if (cancellation.IsCancellationRequested || !await this.InvokeHandlerAsync(ex))
                {
                    throw;
                }

                lastException = ex;
            }

            cancellation.ThrowIfCancellationRequested();
            TimeSpan next = this.NextDelay(startTime, context.CurrentUtcDateTime, attempt);
            if (next == TimeSpan.Zero)
            {
                break;
            }

            await context.CreateTimer(next, default);
        }

        if (lastException is not null)
        {
            ExceptionDispatchInfo.Capture(lastException).Throw();
            throw lastException; // does not get hit.
        }

        return default!;
    }

    Task<bool> InvokeHandlerAsync(Exception ex)
    {
        return this.HandleAsync?.Invoke(ex) ?? Task.FromResult(true);
    }

    TimeSpan NextDelay(DateTimeOffset startTime, DateTimeOffset currentTime, int attempt)
    {
        static bool IsInfinite(TimeSpan value)
        {
            return value == Timeout.InfiniteTimeSpan || value == TimeSpan.MaxValue;
        }

        DateTimeOffset retryExpiration = IsInfinite(this.RetryTimeout)
            ? DateTimeOffset.MaxValue
            : startTime.Add(this.RetryTimeout);

        if (currentTime < retryExpiration)
        {
            try
            {
                double nextDelayInMilliseconds = this.FirstRetryInterval.TotalMilliseconds *
                                                 Math.Pow(this.BackoffCoefficient, attempt);
                return nextDelayInMilliseconds < this.MaxRetryInterval.TotalMilliseconds
                    ? TimeSpan.FromMilliseconds(nextDelayInMilliseconds)
                    : this.MaxRetryInterval;
            }
            catch
            {
                // Swallow exceptions.
                // TODO: log error.
            }
        }

        return TimeSpan.Zero;
    }
}
