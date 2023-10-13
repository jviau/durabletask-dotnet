// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Prototype;

static partial class BaselineRunner
{
    class ChannelRunner : PrototypeRunner<BaselineOptions>
    {
        public ChannelRunner(BaselineOptions options)
            : base(options)
        {
        }

        protected override Task<IHost> CreateHostCoreAsync()
        {
            IHost host = GrpcHost.CreateWorkerHost(
                worker =>
                {
                    worker.Services.AddOrchestrationService("pshim");
                    worker.UseOrchestrationService();
                },
                client =>
                {
                    client.UseOrchestrationService();
                });

            return Task.FromResult(host);
        }

        protected override ValueTask DisposeCoreAsync() => default;
    }
}
