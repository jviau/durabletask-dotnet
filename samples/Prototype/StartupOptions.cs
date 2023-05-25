// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CommandLine;

namespace Microsoft.DurableTask.Prototype;

abstract class StartupOptions
{
    [Option('c', "count", HelpText = "Count of orchestrations to run.")]
    public int Count { get; set; } = 10;

    [Option('d', "depth", HelpText = "Number of activities to run per orchestration.")]
    public int Depth { get; set; } = 5;

    [Option('m', "mode", HelpText = "0 = core runner, bulk gRPC. 1 = channel runner, bulk gRPC. 2 = channel runner, stream gRPC")]
    public int Mode { get; set; } = 0;

    public string Description => this.Mode switch
    {
        0 => "Core runner + bulk gRPC",
        1 => "Channel runner + bulk gRPC",
        2 => "Channel runner + stream gRPC",
        _ => throw new ArgumentException(nameof(this.Mode)),
    };
}

[Verb("local", isDefault: true, HelpText = "Runs the prototype with the hub self-hosted.")]
class SelfHostedOptions : StartupOptions
{
}

[Verb("external", HelpText = "Runs the prototype with the hub externally hosted.")]
class ExternalHostedOptions : StartupOptions
{
    [Option('p', "port", HelpText = "The gRPC port to connect to.")]
    public int Port { get; set; }
}
