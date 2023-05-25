// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Grpc.Net.Client;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

public abstract class GrpcExternalHosted : GrpcBenchmark
{
    Process process = null!;

    protected abstract string Name { get; }

    [GlobalSetup]
    public Task SetupAsync()
    {
        GrpcChannel channel = this.SetupHubProcess();
        return this.SetupCoreAsync(channel);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await this.CleanupCoreAsync();
        this.process.Kill();
        this.process.Dispose();
    }

    GrpcChannel SetupHubProcess()
    {
        int port = Random.Shared.Next(30000, 40000);
        DirectoryInfo? dir = new(Environment.CurrentDirectory);
        while (!string.Equals(dir.Name, "release", StringComparison.Ordinal)
            && !string.Equals(dir.Name, "debug", StringComparison.Ordinal))
        {
            dir = dir.Parent;
            if (dir is null)
            {
                throw new InvalidOperationException();
            }
        }

        string path = Path.Combine(dir.FullName, "Grpc.App", "net6.0", "Microsoft.DurableTask.Grpc.App.exe");
        this.process = Process.Start(path, $"-p {port} -n {this.Name}benchmark \"Logging:LogLevel:Default=Warning\"");
        return GrpcChannel.ForAddress($"http://localhost:{port}");
    }
}
