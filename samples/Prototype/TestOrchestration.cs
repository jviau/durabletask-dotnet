// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Core = DurableTask.Core;

namespace Microsoft.DurableTask.Prototype;

public record TestInput(int Count, string Value);

public class TestOrchestration : TaskOrchestrator<TestInput, object>
{
    public override async Task<object> RunAsync(TaskOrchestrationContext context, TestInput input)
    {
        for (int i = 0; i < input.Count; i++)
        {
            await context.CallActivityAsync<string>(nameof(TestActivity), input.Value);
        }

        return null!;
    }
}

public class TestActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
    {
        return Task.FromResult($"result: {input}");
    }
}

public class TestCoreOrchestration : Core.TaskOrchestration<object, TestInput>
{
    public override async Task<object> RunTask(Core.OrchestrationContext context, TestInput input)
    {
        for (int i = 0; i < input.Count; i++)
        {
            await context.ScheduleTask<string>(typeof(TestCoreActivity), input.Value);
        }

        return null!;
    }
}

public class TestCoreActivity : Core.AsyncTaskActivity<string, string>
{
    protected override Task<string> ExecuteAsync(Core.TaskContext context, string input)
    {
        return Task.FromResult($"result: {input}");
    }
}

