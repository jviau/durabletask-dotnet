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
        static readonly TaskCompletionSource<string?> CompletedSource = new();

        readonly OrchestrationAction action;
        TaskCompletionSource<string?>? tcs;
        bool consumed;

        public PendingAction(OrchestrationAction action)
        {
            this.action = Check.NotNull(action);
            if (action.FireAndForget)
            {
                CompletedSource.TrySetResult(null);
                this.tcs = CompletedSource;
            }
        }

        public bool FireAndForget => this.action.FireAndForget;

        public void Consume(OrchestrationMessage incoming)
        {
            Check.NotNull(incoming);
            if (this.consumed)
            {
                throw new InvalidOperationException(
                    "This pending action has already been consumed by another incoming message.");
            }

            if (!this.action.Matches(incoming))
            {
                throw new InvalidOperationException("Non-deterministic");
            }

            this.consumed = true;
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

        /// <summary>
        /// Waits for this pending action to complete.
        /// </summary>
        /// <returns>The result of the action, if any.</returns>
        public Task<string?> WaitAsync()
        {
            this.EnsureInitialized();
            return this.tcs.Task;
        }

        [MemberNotNull("tcs")]
        void EnsureInitialized()
        {
            this.tcs = LazyInitializer.EnsureInitialized(ref this.tcs)!;
        }
    }
}
