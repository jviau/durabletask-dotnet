// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


using Azure.Identity;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureStorage;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Prototype;

static partial class BaselineRunner
{
    class StreamingRunner : PrototypeRunner<BaselineOptions>
    {
        public StreamingRunner(BaselineOptions options)
            : base(options)
        {
        }

        protected override async Task<IHost> CreateHostCoreAsync()
        {
            IHost host = GrpcHost.CreateWorkerHost(
                worker =>
                {
                    worker.UseAzureStorage("stream2", "storagejavia", new AzureCliCredential());
                },
                client =>
                {
                    client.UseAzureStorage("stream2", "storagejavia", new AzureCliCredential());
                });

            AzureStorageDurableTaskClientOptions options = host.Services
                .GetRequiredService<IOptions<AzureStorageDurableTaskClientOptions>>().Value;

            await options.CreateIfNotExistsAsync();
            return host;
        }

        protected override ValueTask DisposeCoreAsync() => default;
    }
}
