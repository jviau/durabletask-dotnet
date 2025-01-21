// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Azure;

namespace Microsoft.DurableTask.Extensions.Azure.ResourceManager;

/// <summary>
/// An activity request to start an Azure Resource Manager LRO.
/// </summary>
/// <typeparam name="TValue">The resulting value of the LRO.</typeparam>
public interface IArmLroStartReqeust<TValue> : IActivityRequest<ArmLongRunningOperation<TValue>>
    where TValue : notnull
{
    /// <summary>
    /// Gets the name of the <see cref="ArmClient"/> to use for this request.
    /// </summary>
    /// <remarks>
    /// This is the <see cref="ArmClient"/> which will be used to poll the operation.
    /// </remarks>
    string ClientName { get; }
}

/// <summary>
/// A request to create or update an ARM resource.
/// </summary>
/// <typeparam name="TResourceData">The data for the resource.</typeparam>
/// <param name="clientName">The name of the <see cref="ArmClient"/> to use.</param>
/// <param name="id">The identifier of the resource to create or update.</param>
public abstract class CreateOrUpdateResourceRequest<TResourceData>(string clientName, ResourceIdentifier id) : IArmLroStartReqeust<TResourceData>
    where TResourceData : notnull
{
    /// <inheritdoc/>
    public string ClientName { get; } = clientName;

    /// <summary>
    /// Gets the identifier for the resource to operate on.
    /// </summary>
    public ResourceIdentifier Id { get; } = id;

    /// <inheritdoc/>
    public abstract TaskName GetTaskName();
}

/// <summary>
/// Base class for creating ARM LRO activity requests.
/// </summary>
/// <typeparam name="TValue">The value returned by the LRO.</typeparam>
public abstract class ArmLroRequest<TValue> : ILroActivityRequest<ArmLongRunningOperation<TValue>, TValue>
    where TValue : notnull
{
    /// <inheritdoc/>
    public IActivityRequest<ArmLongRunningOperation<TValue>> GetStartRequest() => this.GetStartRequestCore();

    /// <summary>
    /// Gets the request to start this operation.
    /// </summary>
    /// <returns>The request to start the LRO.</returns>
    protected abstract IArmLroStartReqeust<TValue> GetStartRequestCore();
}

/// <summary>
/// Represents an Azure Resource Manager LRO.
/// </summary>
/// <typeparam name="TValue">The value held by the LRO.</typeparam>
public class ArmLongRunningOperation<TValue>
    : ILongRunningOperation<TValue>
    where TValue : notnull
{
    static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ArmLongRunningOperation{TValue}"/> class.
    /// </summary>
    /// <param name="rehydrationToken">The rehydration token.</param>
    /// <param name="clientName">The <see cref="ArmClient"/> name.</param>
    internal ArmLongRunningOperation(RehydrationToken? rehydrationToken, string? clientName)
    {
        this.RehydrationToken = rehydrationToken;
        this.ClientName = clientName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArmLongRunningOperation{TValue}"/> class.
    /// </summary>
    /// <param name="value">The value of the operation.</param>
    internal ArmLongRunningOperation(TValue value)
    {
        this.HasCompleted = true;
        this.Value = value;
    }

    /// <inheritdoc/>
    public bool HasCompleted { get; private set; }

    /// <inheritdoc/>
    public TValue Value { get; private set; } = default!;

    /// <summary>
    /// Gets or sets the retry-after for this operation.
    /// </summary>
    TimeSpan? RetryAfter { get; set; }

    /// <summary>
    /// Gets or sets the operation hydration token.
    /// </summary>
    RehydrationToken? RehydrationToken { get; set; }

    /// <summary>
    /// Gets or sets the poll count.
    /// </summary>
    int PollCount { get; set; }

    /// <summary>
    /// Gets the client name.
    /// </summary>
    string? ClientName { get; }

    /// <inheritdoc/>
    public bool TryGetPollDelay(out TimeSpan pollDelay)
    {
        pollDelay = this.RetryAfter ?? DefaultDelay;
        return true;
    }

    /// <inheritdoc/>
    public async Task UpdateStatusAsync(TaskOrchestrationContext context)
    {
        IActivityRequest<ArmLongRunningOperation<TValue>> request = ArmPollLroActivity<TValue>.CreateRequest(this);
        ArmLongRunningOperation<TValue> operation = await context.RunAsync(request);
        this.HasCompleted = operation.HasCompleted;
        this.Value = operation.Value;
        this.RehydrationToken = operation.RehydrationToken;
        this.PollCount = operation.PollCount;
        this.RetryAfter = operation.RetryAfter;
    }

    /// <summary>
    /// Rehydrates the operation and updates the status.
    /// </summary>
    /// <param name="clients">The client factory to use for status update.</param>
    /// <returns>A task that completes when the status is updated.</returns>
    /// <exception cref="InvalidOperationException">Thrown if this operation is not valid for rehydration.</exception>
    internal async Task UpdateStatusAsync(IAzureClientFactory<ArmClient> clients)
    {
        Check.NotNull(clients);

        if (this.HasCompleted)
        {
            return;
        }

        if (this.RehydrationToken is not { } token)
        {
            throw new InvalidOperationException("Operation has not completed and has no rehydration token.");
        }

        ArmClient client = clients.CreateClient(this.ClientName);
        ArmOperation<TValue> operation = await ArmOperation.RehydrateAsync<TValue>(client, token);
        DelayStrategy strategy = DelayStrategy.CreateFixedDelayStrategy(TimeSpan.FromSeconds(1));
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(1);

        // If the next poll is within a second in the future, lets just delay and do it here.
        while (this.UpdateStatusCore(operation, strategy, deadline, out TimeSpan delay))
        {
            await Task.Delay(delay);
            await operation.UpdateStatusAsync();
        }
    }

    bool UpdateStatusCore(ArmOperation<TValue> operation, DelayStrategy delayStrategy, DateTimeOffset deadline, out TimeSpan delay)
    {
        this.PollCount++;
        if (operation.HasCompleted)
        {
            this.HasCompleted = true;
            this.Value = operation.Value;
            this.RehydrationToken = null;
            return false; // stop polling.
        }
        else if (operation.GetRehydrationToken() is { } newToken)
        {
            this.RehydrationToken = newToken;
        }

        delay = delayStrategy.GetNextDelay(operation.GetRawResponse(), this.PollCount);
        this.RetryAfter = delay;
        return (DateTimeOffset.UtcNow + delay) < deadline; // poll so long as we are before the deadline.
    }
}

/// <summary>
/// An activity to start an Azure LRO.
/// </summary>
/// <typeparam name="TInput">The input to start this LRO with.</typeparam>
/// <typeparam name="TValue">The value of this LRO.</typeparam>
public abstract class ArmStartLroActivity<TInput, TValue> : TaskActivity<TInput, ArmLongRunningOperation<TValue>>
    where TInput : IArmLroStartReqeust<TValue>
    where TValue : notnull
{
    /// <inheritdoc/>
    public sealed override async Task<ArmLongRunningOperation<TValue>> RunAsync(TaskActivityContext context, TInput input)
    {
        ArmOperation<TValue> operation = await this.BeginOperationAsync(context, input);
        if (operation.HasCompleted)
        {
            return new(operation.Value);
        }

        return new(operation.GetRehydrationToken(), input.ClientName);
    }

    /// <summary>
    /// Begins the Azure LRO.
    /// </summary>
    /// <param name="context">The <see cref="TaskActivityContext"/>.</param>
    /// <param name="input">The task input.</param>
    /// <returns>An <see cref="Operation{TValue}"/>.</returns>
    protected abstract Task<ArmOperation<TValue>> BeginOperationAsync(TaskActivityContext context, TInput input);
}

/// <summary>
/// Performs a single poll on an Azure LRO.
/// </summary>
/// <typeparam name="TValue">The result type the LRO returns.</typeparam>
/// <param name="clients">The ARM clients factory.</param>
public sealed class ArmPollLroActivity<TValue>(IAzureClientFactory<ArmClient> clients)
    : TaskActivity<ArmLongRunningOperation<TValue>, ArmLongRunningOperation<TValue>>
    where TValue : notnull
{
    /// <inheritdoc/>
    public override async Task<ArmLongRunningOperation<TValue>> RunAsync(
        TaskActivityContext context, ArmLongRunningOperation<TValue> input)
    {
        Check.NotNull(context);
        Check.NotNull(input);
        await input.UpdateStatusAsync(clients);
        return input;
    }

    /// <summary>
    /// Creates a new <see cref="ArmPollLroActivity{TValue}"/> request.
    /// </summary>
    /// <param name="op">The operation.</param>
    /// <returns>A request to enqueue this activity.</returns>
    internal static IActivityRequest<ArmLongRunningOperation<TValue>> CreateRequest(ArmLongRunningOperation<TValue> op)
        => ActivityRequest.Create<ArmLongRunningOperation<TValue>>(nameof(ArmPollLroActivity<TValue>), op);
}
