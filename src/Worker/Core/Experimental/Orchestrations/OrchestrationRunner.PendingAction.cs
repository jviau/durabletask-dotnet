// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Runs orchestrations.
/// </summary>
partial class OrchestrationRunner
{
    class PendingAction
    {
        TaskCompletionSource<string?>? tcs;
        bool initialized;

        public PendingAction(OrchestrationMessage message)
        {
            this.Message = Check.NotNull(message);
        }

        public OrchestrationMessage Message { get; }

        public void Validate(OrchestrationMessage incoming)
        {
            Check.NotNull(incoming);
            if (this.initialized)
            {
                throw new InvalidOperationException("Already initialized");
            }

            if (incoming != this.Message)
            {
                throw new InvalidOperationException("Non-deterministic");
            }

            this.initialized = true;
        }

        public void Succeed(string? result)
        {
            this.EnsureInitialized();
            this.tcs.TrySetResult(result);
        }

        public void Fail(TaskFailureDetails failure)
        {
            this.EnsureInitialized();
            this.tcs.TrySetException(new InvalidOperationException()); // TODO: fill out exception.
        }

        public async Task<T> GetResultAsync<T>(DataConverter converter)
        {
            this.EnsureInitialized();
            string? result = await this.tcs.Task;

            // orchestrator authors ultimately decide null handling.
            return converter.Deserialize<T>(result)!;
        }

        [MemberNotNull("tcs")]
        void EnsureInitialized()
        {
            this.tcs = LazyInitializer.EnsureInitialized(ref this.tcs)!;
        }
    }
}
