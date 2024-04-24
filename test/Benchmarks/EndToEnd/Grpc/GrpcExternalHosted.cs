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
    public void Setup()
    {
        GrpcChannel channel = this.SetupHubProcess();
        this.SetupCoreAsync(channel).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.CleanupCoreAsync().GetAwaiter().GetResult();
        this.process.Kill();
        this.process.Dispose();
    }

    GrpcChannel SetupHubProcess()
    {
        int port = Random.Shared.Next(30000, 40000);
        DirectoryInfo? dir = new(Environment.CurrentDirectory);
        DirectoryInfo? child = dir.GetDirectories("Grpc.App", SearchOption.TopDirectoryOnly).FirstOrDefault();
        while (child is null)
        {
            dir = dir.Parent;
            if (dir is null)
            {
                throw new InvalidOperationException("Expected folder is missing");
            }

            child = dir.GetDirectories("Grpc.App", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }

        string path = Path.Combine(child.FullName, "net6.0", "Microsoft.DurableTask.Grpc.App.exe");
        this.process = Process.Start(path, $"-b 0 -p {port} -n {this.Name}benchmark \"Logging:LogLevel:Default=Warning\"");
        return GrpcChannel.ForAddress($"http://localhost:{port}");
    }
}
