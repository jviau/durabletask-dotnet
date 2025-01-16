// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Represents the base request to run a <see cref="ITaskOrchestrator" />.
/// </summary>
public interface IBaseOrchestrationRequest
{
    /// <summary>
    /// Gets the <see cref="TaskName" /> representing the <see cref="ITaskOrchestrator" /> to run.
    /// </summary>
    /// <returns>A <see cref="TaskName" />.</returns>
    /// <remarks>
    /// This is a function instead of a property so it is excluded in serialization without needing to use a
    /// serialization library specific attribute to exclude it.
    /// </remarks>
    TaskName GetTaskName();
}

/// <summary>
/// Represents a request to run a <see cref="ITaskOrchestrator" /> which returns <typeparamref name="TResult" />.
/// </summary>
/// <typeparam name="TResult">The result of the orchestrator that is to be ran.</typeparam>
public interface IOrchestrationRequest<out TResult> : IBaseOrchestrationRequest
{
}

/// <summary>
/// Represents a request to run a <see cref="ITaskOrchestrator" /> which has no return.
/// </summary>
public interface IOrchestrationRequest : IOrchestrationRequest<Unit>
{
}

/// <summary>
/// Helpers for creating orchestration requests.
/// </summary>
public static class OrchestrationRequest
{
    /// <summary>
    /// Gets an <see cref="IOrchestrationRequest{TResult}" /> which has an explicitly provided input.
    /// </summary>
    /// <remarks>
    /// This is useful when you want to use an existing type for input (like <see cref="string" />) and not derive an
    /// entirely new type.
    /// </remarks>
    /// <typeparam name="TResult">The result type of the orchestration.</typeparam>
    /// <param name="name">The name of the orchestration to run.</param>
    /// <param name="input">The input for the orchestration.</param>
    /// <returns>A request that can be used to enqueue an orchestration.</returns>
    public static IOrchestrationRequest<TResult> Create<TResult>(TaskName name, object? input = null)
        => new Request<TResult>(name, input);

    /// <summary>
    /// Gets an <see cref="IOrchestrationRequest" /> which has an explicitly provided input.
    /// </summary>
    /// <remarks>
    /// This is useful when you want to use an existing type for input (like <see cref="string" />) and not derive an
    /// entirely new type.
    /// </remarks>
    /// <param name="name">The name of the orchestration to run.</param>
    /// <param name="input">The input for the orchestration.</param>
    /// <returns>A request that can be used to enqueue an orchestration.</returns>
    public static IOrchestrationRequest Create(TaskName name, object? input = null)
        => new Request(name, input);

    /// <summary>
    /// Gets the orchestration input from a <see cref="IBaseOrchestrationRequest" />.
    /// </summary>
    /// <param name="request">The request to get input for.</param>
    /// <returns>The input.</returns>
    internal static object? GetInput(this IBaseOrchestrationRequest request)
    {
        if (request is IProvidesInput provider)
        {
            return provider.GetInput();
        }

        return request;
    }

    class Request<TResult>(TaskName name, object? input) : RequestCore(name, input), IOrchestrationRequest<TResult>
    {
    }

    class Request(TaskName name, object? input) : RequestCore(name, input), IOrchestrationRequest
    {
    }

    class RequestCore(TaskName name, object? input) : IBaseOrchestrationRequest, IProvidesInput
    {
        public object? GetInput() => input;

        public TaskName GetTaskName() => name;
    }
}
