// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Microsoft.DurableTask;

IConfig config = ManualConfig.Create(DefaultConfig.Instance)
    .WithOptions(ConfigOptions.JoinSummary)
    .WithOptions(ConfigOptions.DisableLogFile);

BenchmarkSwitcher.FromAssembly(typeof(AssemblyMarker).Assembly).Run(args, config);

namespace Microsoft.DurableTask
{
    class AssemblyMarker { }
}
