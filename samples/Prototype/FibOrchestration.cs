// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Core = DurableTask.Core;

namespace Microsoft.DurableTask.Prototype;

public static class Fib
{
    public static int Get(int n)
    {
        static int Core(int n)
        {
            (int a, int b) = (0, 1);
            while (n-- > 1)
            {
                (a, b) = (b, b + a);
            }

            return b;
        }

        return n switch
        {
            0 => 0,
            1 => 1,
            _ => Core(n),
        };
    }
}

public class FibOrchestration : TaskOrchestrator<int, int>
{
    public override async Task<int> RunAsync(TaskOrchestrationContext context, int input)
    {
        static async Task<int> FibAsync(TaskOrchestrationContext context, int count)
        {
            Task<int> first = context.CallSubOrchestratorAsync<int>(nameof(FibOrchestration), count - 1);
            Task<int> second = context.CallSubOrchestratorAsync<int>(nameof(FibOrchestration), count - 2);
            return (await first) + (await second);
        }

        return input switch
        {
            0 or 1 => await context.CallActivityAsync<int>(nameof(FibEndActivity), input),
            _ => await FibAsync(context, input),
        };
    }
}

public class FibEndActivity : TaskActivity<int, int>
{
    static readonly Task<int> Zero = Task.FromResult(0);
    static readonly Task<int> One = Task.FromResult(1);

    public override Task<int> RunAsync(TaskActivityContext context, int input)
    {
        return input switch
        {
            0 => Zero,
            1 => One,
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
    }
}

public class FibCoreOrchestration : Core.TaskOrchestration<int, int>
{
    public override async Task<int> RunTask(Core.OrchestrationContext context, int input)
    {
        static async Task<int> FibAsync(Core.OrchestrationContext context, int count)
        {
            Task<int> first = context.CreateSubOrchestrationInstance<int>(typeof(FibCoreOrchestration), count - 1);
            Task<int> second = context.CreateSubOrchestrationInstance<int>(typeof(FibCoreOrchestration), count - 2);
            return (await first) + (await second);
        }

        return input switch
        {
            0 or 1 => await context.ScheduleTask<int>(typeof(FibEndCoreActivity), input),
            _ => await FibAsync(context, input),
        };
    }
}

public class FibEndCoreActivity : Core.AsyncTaskActivity<int, int>
{
    static readonly Task<int> Zero = Task.FromResult(0);
    static readonly Task<int> One = Task.FromResult(1);

    protected override Task<int> ExecuteAsync(Core.TaskContext context, int input)
    {
        return input switch
        {
            0 => Zero,
            1 => One,
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
    }
}

