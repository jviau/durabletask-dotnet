// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Prototype;
using CommandLine;

await using Runner runner = Parser.Default.ParseArguments<
    SelfHostedOptions, ExternalHostedOptions, BaselineOptions>(args)
    .MapResult<SelfHostedOptions, ExternalHostedOptions, BaselineOptions, Runner>(
        o => new SelfHostedRunner(o),
        o => new ExternallyHostedRunner(o),
        o => new BaselineRunner(o),
        errors =>
        {
            foreach (Error e in errors)
            {
                Console.WriteLine(e);
            }

            throw new InvalidOperationException();
        });

await runner.RunAsync();
