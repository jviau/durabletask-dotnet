// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Grpc.Hub;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Tests;

/// <summary>
/// Base class for integration tests that use a in-process sidecar for executing orchestrations.
/// </summary>
public class IntegrationTestBase : IDisposable
{
    readonly CancellationTokenSource testTimeoutSource
        = new(Debugger.IsAttached ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(10));

    readonly TestLogProvider logProvider;

    // Documentation on xunit test fixtures: https://xunit.net/docs/shared-context

    public IntegrationTestBase(ITestOutputHelper output)
    {
        this.logProvider = new(output);
    }

    /// <summary>
    /// Gets a <see cref="CancellationToken"/> that triggers after a default test timeout period.
    /// The actual timeout value is increased if a debugger is attached to the test process.
    /// </summary>
    public CancellationToken TimeoutToken => this.testTimeoutSource.Token;

    void IDisposable.Dispose()
    {
        this.testTimeoutSource.Dispose();
        GC.SuppressFinalize(this);
    }

    protected async Task<HostTestLifetime> StartWorkerAsync(Action<IDurableTaskWorkerBuilder> configure)
    {
        IHost host = this.CreateHostBuilder(configure).Build();
        await host.StartAsync(this.TimeoutToken);
        return new HostTestLifetime(host, this.TimeoutToken);
    }

    /// <summary>
    /// Creates a <see cref="IHostBuilder"/> configured to output logs to xunit logging infrastructure.
    /// </summary>
    /// <param name="configure">Configures the durable task builder.</param>
    protected IHostBuilder CreateHostBuilder(Action<IDurableTaskWorkerBuilder> configure)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(b =>
            {
                b.ClearProviders();
                b.AddProvider(this.logProvider);
                b.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices(services =>
            {
                services.AddInMemoryOrchestrationService();
                services.AddDurableTaskWorker(b =>
                {
                    b.UseOrchestrationService();
                    configure(b);
                });

                services.AddDurableTaskClient(b =>
                {
                    b.UseOrchestrationService();
                    b.RegisterDirectly();
                });
            });
    }

    protected IReadOnlyCollection<LogEntry> GetLogs()
    {
        // NOTE: Renaming the log category is a breaking change!
        const string ExpectedCategoryName = "Microsoft.DurableTask";
        bool foundCategory = this.logProvider.TryGetLogs(ExpectedCategoryName, out IReadOnlyCollection<LogEntry> logs);
        Assert.True(foundCategory);
        return logs;
    }

    protected readonly struct HostTestLifetime : IAsyncDisposable
    {
        readonly IHost host;
        readonly CancellationToken cancellation;

        public HostTestLifetime(IHost host, CancellationToken cancellation)
        {
            this.host = host;
            this.cancellation = cancellation;
            this.Client = host.Services.GetRequiredService<DurableTaskClient>();
        }

        public DurableTaskClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await this.host.StopAsync(this.cancellation);
            }
            finally
            {
                this.host.Dispose();
            }
        }
    }
}
