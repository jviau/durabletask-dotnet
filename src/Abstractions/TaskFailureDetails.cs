// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CoreOrchestrationException = DurableTask.Core.Exceptions.OrchestrationException;

namespace Microsoft.DurableTask;

/// <summary>
/// Record that represents the details of a task failure.
/// </summary>
/// <param name="ErrorType">The error type. For .NET, this is the namespace-qualified exception type name.</param>
/// <param name="ErrorMessage">A summary description of the failure.</param>
/// <param name="StackTrace">The stack trace of the failure.</param>
/// <param name="InnerFailure">The inner cause of the task failure.</param>
public record TaskFailureDetails(string ErrorType, string ErrorMessage, string? StackTrace, TaskFailureDetails? InnerFailure)
{
    readonly CausedByContainer container = new();

    /// <summary>
    /// Gets a debug-friendly description of the failure information.
    /// </summary>
    /// <returns>A debugger friendly display string.</returns>
    public override string ToString()
    {
        return $"{this.ErrorType}: {this.ErrorMessage}";
    }

    /// <summary>
    /// Returns <c>true</c> if the task failure was provided by the specified exception type.
    /// </summary>
    /// <remarks>
    /// This method allows checking if a task failed due to an exception of a specific type by attempting
    /// to load the type specified in <see cref="ErrorType"/>. If the exception type cannot be loaded
    /// for any reason, this method will return <c>false</c>. Base types are supported.
    /// </remarks>
    /// <typeparam name="T">The type of exception to test against.</typeparam>
    /// <returns>
    /// Returns <c>true</c> if the <see cref="ErrorType"/> value matches <typeparamref name="T"/>; <c>false</c> otherwise.
    /// </returns>
    public bool IsCausedBy<T>() where T : Exception
    {
        Type? t = this.container.GetOrInitialize(this);
        return t is not null && typeof(T).IsAssignableFrom(t);
    }

    /// <summary>
    /// Creates a task failure details from an <see cref="Exception" />.
    /// </summary>
    /// <param name="exception">The exception to use.</param>
    /// <returns>A new task failure details.</returns>
    /// <remarks>Does not include stack trace.</remarks>
    public static TaskFailureDetails FromException(Exception exception) => FromException(exception, false);

    /// <summary>
    /// Creates a task failure details from an <see cref="Exception" />.
    /// </summary>
    /// <param name="exception">The exception to use.</param>
    /// <param name="includeStackTrace"><c>true</c> to include stack trace, <c>false</c> to leave out.</param>
    /// <returns>A new task failure details.</returns>
    public static TaskFailureDetails FromException(Exception exception, bool includeStackTrace)
    {
        Check.NotNull(exception);
        if (exception is CoreOrchestrationException coreEx)
        {
            return new TaskFailureDetails(
                coreEx.FailureDetails?.ErrorType ?? "(unknown)",
                coreEx.FailureDetails?.ErrorMessage ?? "(unknown)",
                coreEx.FailureDetails?.StackTrace,
                null /* InnerFailure */);
        }

        if (exception is TaskFailedException failed)
        {
            return new TaskFailureDetails(
                exception.GetType().ToString(),
                exception.Message,
                includeStackTrace ? exception.StackTrace : null,
                failed.FailureDetails);
        }

        // TODO: consider populating inner details.
        return new TaskFailureDetails(
            exception.GetType().ToString(),
            exception.Message,
            includeStackTrace ? exception.StackTrace : null,
            null);
    }

    class CausedByContainer : IEquatable<CausedByContainer>
    {
        // Helper class to hold exceptions but not use them in equality comparison.
        bool initialized;
        Type? exceptionType;

        public Type? GetOrInitialize(TaskFailureDetails details)
        {
            static Type? Initialize(TaskFailureDetails details)
            {
                Type t = Type.GetType(details.ErrorType, throwOnError: false);
                if (details.InnerFailure is null || !typeof(TaskFailedException).IsAssignableFrom(t))
                {
                    return t;
                }

                return Initialize(details.InnerFailure);
            }

            if (this.initialized)
            {
                return this.exceptionType;
            }

            lock (this)
            {
                if (this.initialized)
                {
                    return this.exceptionType;
                }

                this.exceptionType = Initialize(details);
                this.initialized = true;
            }

            return this.exceptionType;
        }

        public bool Equals(CausedByContainer other) => true;

        public override bool Equals(object obj) => obj is CausedByContainer;

        public override int GetHashCode() => typeof(CausedByContainer).GetHashCode();
    }
}
