// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Prototype;
using CommandLine;

await using IRunner runner = Parser.Default.ParseArguments<SelfHostedOptions, ExternalHostedOptions>(args)
    .MapResult<SelfHostedOptions, ExternalHostedOptions, IRunner>(
        o => new SelfHosted(o),
        o => new ExternallyHosted(o),
        errors =>
        {
            foreach (Error e in errors)
            {
                Console.WriteLine(e);
            }

            throw new InvalidOperationException();
        });

await runner.RunAsync();
