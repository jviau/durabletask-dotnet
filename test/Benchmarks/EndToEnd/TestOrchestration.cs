// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

public record TestInput(int Count, string Value);

public class TestOrchestration : TaskOrchestrator<TestInput, IEnumerable<string>>
{
    public override async Task<IEnumerable<string>> RunAsync(TaskOrchestrationContext context, TestInput input)
    {
        string[] result = new string[input.Count];
        for (int i = 0; i < input.Count; i++)
        {
            result[0] = await context.CallActivityAsync<string>(nameof(TestActivity), input with { Count = i });
        }

        return result;
    }
}

public class TestActivity : TaskActivity<TestInput, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, TestInput input)
    {
        string result = string.Empty;
        for (int i = 0; i < input.Count; i++)
        {
            result += input.Value;
        }

        return Task.FromResult(result);
    }
}

