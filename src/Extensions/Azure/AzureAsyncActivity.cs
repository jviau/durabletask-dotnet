// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;

namespace Microsoft.DurableTask.Extensions.Azure;

public abstract class AzureAsyncActivityRequest : IActivityRequest<Operation>
{
    /// <summary>
    /// Gets or sets the poll interval. Default is 30 seconds if not set.
    /// </summary>
    public TimeSpan? PollInterval { get; set; }

    /// <summary>
    /// Gets or sets what state to wait for the operation to reach before returning.
    /// <see cref="WaitUntil.Completed" /> to wait for the operation to fully complete (Default).
    /// <see cref="WaitUntil.Started" /> to wait until the operation has start. Return will be <c>null</c> in this case.
    /// </summary>
    public WaitUntil WaitUntil { get; set; } = WaitUntil.Completed;

    public virtual IOrchestrationRequest GetOrchestrationRequest() => new AzureAsyncOrchestrationRequest(this);

    /// <inheritdoc/>
    public abstract TaskName GetTaskName();
}

public class AzureAsyncOrchestrationRequest : IOrchestrationRequest
{
    public AzureAsyncOrchestrationRequest(AzureAsyncActivityRequest activity)
    {
        this.Activity = activity;
    }

    public AzureAsyncActivityRequest Activity { get; }

    public TaskName GetTaskName() => nameof(AzureAsyncOrchestrator);
}

public class PollOperationActivity : TaskActivity<Operation, Operation>
{
    public static IActivityRequest<Operation> CreateRequest(Operation operation)
        => ActivityRequest.Create<Operation>(nameof(PollOperationActivity), operation);

    public override async Task<Operation> RunAsync(TaskActivityContext context, Operation input)
    {
        Check.NotNull(input);
        await input.UpdateStatusAsync();
        return input;
    }
}

public class AzureAsyncOrchestrator : TaskOrchestrator<AzureAsyncOrchestrationRequest, Stream?>
{
    public override async Task<Stream?> RunAsync(
        TaskOrchestrationContext context, AzureAsyncOrchestrationRequest input)
    {
        Check.NotNull(context);
        Check.NotNull(input);

        Operation operation = await context.RunAsync(input.Activity);

        if (!operation.HasCompleted)
        {
            operation = await context.RunAsync(new WaitOperationOrchestrationRequest(
                operation, input.Activity.PollInterval));
        }

        Response response = operation.GetRawResponse();
        return response.ContentStream;
    }
}

public record WaitOperationOrchestrationRequest(Operation Operation, TimeSpan? PollInterval = null)
    : IOrchestrationRequest<Operation>
{
    public TaskName GetTaskName() => nameof(WaitOperationOrchestrator);
}

public class WaitOperationOrchestrator : TaskOrchestrator<WaitOperationOrchestrationRequest, Operation>
{
    /// <inheritdoc/>
    public override async Task<Operation> RunAsync(
        TaskOrchestrationContext context, WaitOperationOrchestrationRequest input)
    {
        Check.NotNull(context);
        Check.NotNull(input);

        Operation op = input.Operation;
        op = await context.RunAsync(PollOperationActivity.CreateRequest(op));

        if (op.HasCompleted)
        {
            return op;
        }

        TimeSpan interval = input.PollInterval ?? TimeSpan.FromSeconds(30);
        await context.CreateTimer(interval, default);
        context.ContinueAsNew(input with { Operation = op });
        return op; // return ignored.
    }
}

public static class AzureTaskOrchestrationContextExtensions
{
    public static Task<TOutput> RunAsync<TOutput>(
        this TaskOrchestrationContext context,
        IActivityRequest<Operation<TOutput>> request,
        TaskOptions? options = null)
    {
        Check.NotNull(context);
        Check.NotNull(request);
        throw new NotImplementedException();
    }

    public static Task RunAsync(
        this TaskOrchestrationContext context, AzureAsyncActivityRequest request, TaskOptions? options = null)
    {
        Check.NotNull(context);
        Check.NotNull(request);

        if (request.WaitUntil == WaitUntil.Completed)
        {
        }
    }
}
