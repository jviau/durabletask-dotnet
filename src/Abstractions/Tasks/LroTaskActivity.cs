// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// A request for a long-running operation activity.
/// </summary>
/// <typeparam name="TOperation">The operation for this activity.</typeparam>
/// <typeparam name="TValue">The value of the activity.</typeparam>
public interface ILroActivityRequest<TOperation, out TValue>
    where TOperation : ILongRunningOperation<TValue>
{
    /// <summary>
    /// Gets the <see cref="IActivityRequest{TOperation}"/> to begin the LRO.
    /// </summary>
    /// <returns>The request that will be used to start the operation.</returns>
    IActivityRequest<TOperation> GetStartRequest();
}

/// <summary>
/// Represents a long-running operation that is polled via a <see cref="LroOrchestrationRequest{TOperation, TValue}"/>.
/// </summary>
/// <typeparam name="TValue">The value held by the operation.</typeparam>
public interface ILongRunningOperation<out TValue>
{
    /// <summary>
    /// Gets a value indicating whether the operation has completed or not.
    /// </summary>
    bool HasCompleted { get; }

    /// <summary>
    /// Gets the value of the operation if it has completed.
    /// </summary>
    TValue Value { get; }

    /// <summary>
    /// Gets the delay before the next poll of the operation.
    /// Returning <c>false</c> will skip any delay before the next poll.
    /// </summary>
    /// <param name="pollDelay">The delay before the next poll.</param>
    /// <returns><c>true</c> to delay before the next poll, <c>false</c> to have no delay.</returns>
    bool TryGetPollDelay(out TimeSpan pollDelay);

    /// <summary>
    /// Updates the state of this operation.
    /// </summary>
    /// <param name="context">The orchestration context to use to update state.</param>
    /// <returns>A task that completes when state is updated.</returns>
    Task UpdateStatusAsync(TaskOrchestrationContext context);
}

/// <summary>
/// Extensions for <see cref="DurableTaskRegistry"/>.
/// </summary>
public static class LroDurableTaskRegistryExtensions
{
    /// <summary>
    /// Registers the orchestrators for long-running operations of a given type.
    /// </summary>
    /// <typeparam name="TOperation">The operation type.</typeparam>
    /// <typeparam name="TValue">The result of the operation.</typeparam>
    /// <param name="registry">The durable registry.</param>
    /// <returns>The <paramref name="registry"/> for call chaining.</returns>
    public static DurableTaskRegistry AddLroTasks<TOperation, TValue>(this DurableTaskRegistry registry)
        where TOperation : ILongRunningOperation<TValue>
    {
        Check.NotNull(registry);
        registry.AddOrchestrator<LroOrchestrationRequest<TOperation, TValue>.Handler>(nameof(LroOrchestrationRequest<TOperation, TValue>));
        registry.AddOrchestrator<WaitLroOrchestrationRequest<TOperation, TValue>.Handler>(nameof(WaitLroOrchestrationRequest<TOperation, TValue>));

        return registry;
    }
}

/// <summary>
/// Extensions for <see cref="ILroActivityRequest{TOperation, TValue}"/> on <see cref="TaskOrchestrationContext"/>.
/// </summary>
public static class LroRequestExtensions
{
    /// <summary>
    /// Runs a long-running operation orchestration.
    /// </summary>
    /// <typeparam name="TOperation">The type representing the LRO.</typeparam>
    /// <typeparam name="TOutput">The output of this LRO.</typeparam>
    /// <param name="context">The orchestration context to schedule the activity to.</param>
    /// <param name="request">The activity request to schedule.</param>
    /// <returns>The output of the LRO.</returns>
    public static Task<TOutput> RunAsync<TOperation, TOutput>(this TaskOrchestrationContext context, ILroActivityRequest<TOperation, TOutput> request)
    where TOperation : ILongRunningOperation<TOutput>
    {
        Check.NotNull(context);
        Check.NotNull(request);

        LroOrchestrationRequest<TOperation, TOutput> orchestration = new(request);
        return context.RunAsync(orchestration);
    }
}

/// <summary>
/// A request for a long-running operation orchestration.
/// </summary>
/// <typeparam name="TOperation">The operation type.</typeparam>
/// <typeparam name="TValue">The final result of the operation.</typeparam>
/// <param name="activity">The activity to start and poll the operation.</param>
public class LroOrchestrationRequest<TOperation, TValue>(ILroActivityRequest<TOperation, TValue> activity)
    : IOrchestrationRequest<TValue>
    where TOperation : ILongRunningOperation<TValue>
{
    /// <summary>
    /// Gets the activity to start the LRO.
    /// </summary>
    public ILroActivityRequest<TOperation, TValue> Activity { get; } = activity;

    /// <inheritdoc/>
    public virtual TaskName GetTaskName() => nameof(LroOrchestrationRequest<TOperation, TValue>);

    /// <summary>
    /// Base handler for this LRO.
    /// </summary>
    /// <typeparam name="TInput">The <see cref="LroOrchestrationRequest{TOperation, TValue}"/> input.</typeparam>
    public abstract class HandlerBase<TInput> : TaskOrchestrator<TInput, TValue>
        where TInput : LroOrchestrationRequest<TOperation, TValue>
    {
        /// <inheritdoc />
        public override async Task<TValue> RunAsync(TaskOrchestrationContext context, TInput input)
        {
            Check.NotNull(input);

            TOperation operation = await context.RunAsync(input.Activity.GetStartRequest());
            if (operation.HasCompleted)
            {
                return operation.Value;
            }

            IOrchestrationRequest<TValue> waitRequest = new WaitLroOrchestrationRequest<TOperation, TValue>(operation);
            return await context.RunAsync(waitRequest);
        }
    }

    /// <summary>
    /// Default implementation for <see cref="HandlerBase{TInput}"/>.
    /// </summary>
    public sealed class Handler : HandlerBase<LroOrchestrationRequest<TOperation, TValue>>
    {
    }
}

/// <summary>
/// An orchestration request to wait for a long-running operation to complete.
/// </summary>
/// <typeparam name="TOperation">The operation to wait on.</typeparam>
/// <typeparam name="TValue">The final output of the operation.</typeparam>
/// <param name="operation">The operation to poll.</param>
sealed class WaitLroOrchestrationRequest<TOperation, TValue>(TOperation operation)
    : IOrchestrationRequest<TValue>
    where TOperation : ILongRunningOperation<TValue>
{
    /// <summary>
    /// Gets the operation being monitored.
    /// </summary>
    public TOperation Operation { get; } = operation;

    /// <inheritdoc/>
    public TaskName GetTaskName() => nameof(WaitLroOrchestrationRequest<TOperation, TValue>);

    /// <summary>
    /// The handler for this orchestration.
    /// </summary>
    public sealed class Handler : TaskOrchestrator<WaitLroOrchestrationRequest<TOperation, TValue>, TValue>
    {
        /// <inheritdoc/>
        public override async Task<TValue> RunAsync(TaskOrchestrationContext context, WaitLroOrchestrationRequest<TOperation, TValue> input)
        {
            Check.NotNull(context);
            Check.NotNull(input);

            if (input.Operation.TryGetPollDelay(out TimeSpan delay))
            {
                await context.CreateTimer(delay, default);
            }

            await input.Operation.UpdateStatusAsync(context);
            if (input.Operation.HasCompleted)
            {
                return input.Operation.Value;
            }

            context.ContinueAsNew(input);
            return default!;
        }
    }
}
