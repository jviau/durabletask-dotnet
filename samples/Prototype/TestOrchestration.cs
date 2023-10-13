// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Core = DurableTask.Core;

namespace Microsoft.DurableTask.Prototype;

public class TestOrchestration : TaskOrchestrator<int, int>
{
    public override async Task<int> RunAsync(TaskOrchestrationContext context, int input)
    {
        int count = 0;
        for (int i = 0; i < input; i++)
        {
            count = await context.CallActivityAsync<int>(nameof(TestActivity), count);
        }

        return count;
    }
}

public class TestActivity : TaskActivity<int, int>
{
    public override Task<int> RunAsync(TaskActivityContext context, int input)
    {
        return Task.FromResult(input + 1);
    }
}

public class TestCoreOrchestration : Core.TaskOrchestration<int, int>
{
    public override async Task<int> RunTask(Core.OrchestrationContext context, int input)
    {
        int count = 0;
        for (int i = 0; i < input; i++)
        {
            count = await context.ScheduleTask<int>(typeof(TestCoreActivity), count);
        }

        return count;
    }
}

public class TestCoreActivity : Core.AsyncTaskActivity<int, int>
{
    protected override Task<int> ExecuteAsync(Core.TaskContext context, int input)
    {
        return Task.FromResult(input + 1);
    }
}
