// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Prototype;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using DurableTask.Core;

int count = int.Parse(args[0]);
int depth = int.Parse(args[1]);
int mode = int.Parse(args[2]);

string description;

IWebHost hub;
IHost worker;
if (mode == 0)
{
    Console.WriteLine("Core mode");
    description = "Core runner + bulk gRPC";
    hub = GrpcHost.CreateBulkHubHost("core", out GrpcChannel channel);
    worker = GrpcHost.CreateWorkerHost(
                b => b.UseGrpc(channel),
                b => b.UseGrpc(channel).RegisterDirectly());
}
else if (mode == 1)
{
    Console.WriteLine("Bulk mode");
    description = "Channel runner + bulk gRPC";
    hub = GrpcHost.CreateBulkHubHost("bulk", out GrpcChannel channel);
    worker = GrpcHost.CreateWorkerHost(
                b => b.UseBulkGrpcChannel(channel),
                b => b.UseGrpc(channel).RegisterDirectly());
}
else if (mode == 2)
{
    Console.WriteLine("Stream mode");
    description = "Channel runner + stream gRPC";
    hub = GrpcHost.CreateStreamHubHost(out GrpcChannel channel);
    worker = GrpcHost.CreateWorkerHost(
                b => b.UseStreamGrpcChannel(channel),
                b => b.UseStreamGrpc(channel).RegisterDirectly());
}
else
{
    throw new ArgumentException();
}

Console.WriteLine("Starting hub.");
await hub.StartAsync();
Console.WriteLine("Hub started.");
Console.WriteLine("Starting worker.");
await worker.StartAsync();
Console.WriteLine("Worker started.");

IOrchestrationService service = hub.Services.GetRequiredService<IOrchestrationService>();
IOrchestrationServiceClient serviceClient = hub.Services.GetRequiredService<IOrchestrationServiceClient>();
IHostApplicationLifetime lifetime = worker.Services.GetRequiredService<IHostApplicationLifetime>();
await service.CreateIfNotExistsAsync();

DurableTaskClient client = worker.Services.GetRequiredService<DurableTaskClient>();

async Task RunOrchestrationAsync(int i, int depth)
{
    await Task.Yield();
    string id = await client.ScheduleNewOrchestrationInstanceAsync(
        nameof(TestOrchestration), new TestInput(depth, "test-value"));
    await serviceClient.WaitForOrchestrationAsync(id, null, TimeSpan.MaxValue, lifetime.ApplicationStopping);
}

async Task<TimeSpan> RunIterationAsync(int count, int depth)
{
    Stopwatch sw = Stopwatch.StartNew();
    Task[] tasks = new Task[count];
    for (int i = 0; i < count; i++)
    {
        tasks[i] = RunOrchestrationAsync(i, depth);
    }

    await Task.WhenAll(tasks).WaitAsync(lifetime.ApplicationStopping);

    sw.Stop();

    return sw.Elapsed;
}

async Task CleanupAsync()
{
    IOrchestrationServicePurgeClient purgeClient = (IOrchestrationServicePurgeClient)serviceClient;
    PurgeInstanceFilter filter = new(DateTime.MinValue, null, new[]
    {
        OrchestrationStatus.Pending,
        OrchestrationStatus.Running,
        OrchestrationStatus.Terminated,
        OrchestrationStatus.Suspended,
        OrchestrationStatus.Failed,
        OrchestrationStatus.Completed,
        OrchestrationStatus.ContinuedAsNew,
        OrchestrationStatus.Canceled,
     });

    await purgeClient.PurgeInstanceStateAsync(filter);
}

// warmup
for (int i = 0; i < 5; i++)
{
    Console.WriteLine($"Warming up {i + 1}/5");
    await RunIterationAsync(i, 1);
    await CleanupAsync();
}

TimeSpan[] times = new TimeSpan[10];
for (int i = 0; i < 10; i++)
{
    times[i] = await RunIterationAsync(count, depth);
    Console.WriteLine($"Itreration {i + 1}/10 -- {times[i].TotalMilliseconds}");
    await CleanupAsync();
}

foreach (TimeSpan t in times)
{
    Console.WriteLine(t);
}

var ms = times.Select(t => t.TotalMilliseconds).ToList();
double avg = ms.Average();
double stdDev = Math.Sqrt(ms.Average(v => Math.Pow(v - avg, 2)));
Console.WriteLine($"{description} -- Average: {avg}. StdDev: {stdDev}");

await worker.StopAsync();
Console.WriteLine($"Worker stopped");
await hub.StopAsync();
Console.WriteLine($"Hub stopped");
