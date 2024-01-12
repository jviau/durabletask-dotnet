// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CommandLine;

namespace Microsoft.DurableTask.Grpc.App;

/// <summary>
/// Startup options for the DurableTask hub app.
/// </summary>
public class StartupOptions
{
    /// <summary>
    /// Gets or sets the gRPC port to listen on.
    /// </summary>
    [Option('p', "port", HelpText = "The gRPC port to listen on.")]
    public int? Port { get; set; }

    /// <summary>
    /// Gets or sets the TaskHub name to use.
    /// </summary>
    [Option('n', "name", HelpText = "The TaskHub name to use.")]
    public string Name { get; set; } = "testhub";

    /// <summary>
    /// Gets or sets the backend type to use.
    /// </summary>
    [Option('b', "backend", HelpText = "The backend to use. 0 for in memory, 1 for Azure storage.")]
    public int Backend { get; set; }
}
