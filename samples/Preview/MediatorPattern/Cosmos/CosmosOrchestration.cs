// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Microsoft.Azure.Cosmos;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Extensions.CosmosDb;
using Microsoft.Extensions.Logging;

namespace Preview.MediatorPattern.Cosmos;

/**
* This sample shows how mediator-pattern orchestrations and activities can be leveraged for advanced scenarios
* with seemless consumption. In this case, we are enqueueing a 'single' activity which will actually be
* multiple activities, correctly paging the results of a CosmosDB query to the parent orchestration.
*
* This uses IAsyncEnumerable{T}, so all behaviors with that enumerable work as expected.
*/
public record Document(string Name);

public static class CosmosConstants
{
    public const string Database = "my_db";

    public const string Container = "my_container";
}

/// <summary>
/// This orchestrator shows invoking a CosmosDB query and getting an <see cref="IAsyncEnumerable{T}"/> back. As the
/// enumerable is iterated over, activites will be dispatched as needed to fetch each page.
/// </summary>
public class CosmosOrchestrator : TaskOrchestrator
{
    static readonly QueryDefinition query = new("SELECT * FROM c WHERE STARTSWITCH(c.Name, 'example')");

    public static IOrchestrationRequest CreateRequest() => OrchestrationRequest.Create(nameof(CosmosOrchestrator));

    public override async Task RunAsync(TaskOrchestrationContext context)
    {
        ILogger logger = context.CreateReplaySafeLogger<CosmosOrchestrator>();
        int i = 0;
        await foreach (Document d in context.RunAsync(new QueryContainerActivity<Document>(query, CosmosConstants.Container)))
        {
            logger.LogInformation("Retrieved document {Index} with name {Name}", i, d.Name);
            i++;
        }
    }

}

/// <summary>
/// This orchestrator shows invoking a CosmosDB query, where each page is handled individually. After each page, the orchestrator
/// will be continued-as-new to process the next page. An implementation-defined value is passed along to the next invocation,
/// allowing for aggregating some result across all pages. This approach optimizes away replays of the same page.
/// </summary>
public class CosmosAggregationOrchestrator : PagedTaskOrchestration<CosmosAggregationOrchestrator.Request, Document, string>
{
    static readonly QueryDefinition query = new("SELECT * FROM c WHERE STARTSWITCH(c.Name, 'example')");

    public static IOrchestrationRequest<string> CreateRequest() => new Request();

    protected override ValueTask<string> RunAsync(TaskOrchestrationContext context, Request input, Page<Document>? page)
    {
        if (page is null)
        {
            return new("No results");
        }

        ILogger logger = context.CreateReplaySafeLogger<CosmosAggregationOrchestrator>();
        int i = 0;

        StringBuilder builder = new(input.Value);
        foreach (Document d in page.Values)
        {
            logger.LogInformation("Retrieved document {Index} with name {Name}", i, d.Name);
            i++;

            builder.Append($", {d.Name}");
        }

        return new(builder.ToString());
    }

    public class Request : RequestBase
    {
        internal string? Value { get; set; }

        string? ContinuationToken { get; set; }

        public override Request GetNextRequest(string current, string? continuationToken)
        {
            this.Value = current;
            this.ContinuationToken = continuationToken;
            return this;
        }

        public override IPagedActivityRequest<Document> GetPagedActivityRequest()
        {
            return new QueryContainerActivity<Document>(query, CosmosConstants.Container) { ContinuationToken = this.ContinuationToken };
        }

        public override TaskName GetTaskName() => nameof(CosmosAggregationOrchestrator);
    }
}
