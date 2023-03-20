// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Extensions.Cosmos;

namespace Preview.Cosmos;

public class GetItemsOrchestration_AsyncEnumerable : TaskOrchestrator<string?>
{
    public static IOrchestrationRequest CreateRequest(string? continuationToken = null)
        => OrchestrationRequest.Create(nameof(GetItemsOrchestration_AsyncEnumerable), continuationToken);

    public override async Task RunAsync(TaskOrchestrationContext context, string? input)
    {
        QueryItemsActivityRequest<MyDbItem> request = new(
            "test_db", "test_container", MyDbItem.StartsWithQuery("test"))
            {
                ContinuationToken = input,
            };

        await foreach (MyDbItem item in context.RunAsync(request))
        {
            // do something with item
        }
    }
}

public class GetItemsOrchestration_ContinueAsNew : TaskOrchestrator<string?>
{
    public static IOrchestrationRequest CreateRequest(string? continuationToken)
        => OrchestrationRequest.Create(nameof(GetItemsOrchestration_ContinueAsNew), continuationToken);

    public override async Task RunAsync(TaskOrchestrationContext context, string? input)
    {
        QueryItemsActivityRequest<MyDbItem> request = new(
            "test_db", "test_container", MyDbItem.StartsWithQuery("test"))
        {
            ContinuationToken = input,
        };

        Page<MyDbItem> page = await context.RunAsync(request.GetPage());
        foreach (MyDbItem item in page.Values)
        {
            // do something with item
        }

        if (page.ContinuationToken is string continuation)
        {
            context.ContinueAsNew(continuation);
        }
    }
}

public class MyDbItem
{
    public string Name { get; set; } = string.Empty;

    public static QueryDefinition StartsWithQuery(string prefix)
    {
        return new QueryDefinition("SELECT * FROM d WHERE STARTSWITH(d.Name, @prefix)")
            .WithParameter("@prefix", prefix);
    }
}
