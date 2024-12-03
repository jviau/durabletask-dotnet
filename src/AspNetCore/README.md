# Microsoft.DurableTask.AspNetCore

Adds `Microsoft.DurableTask` integrations to AspNetCore.

Commonly used types:
- `OrchestrationStatusHttpResult`
  - via `Results.Extensions.OrchestrationStatus(string instanceId)`
- `OrchestrationStatusResult`

## Quick Start

``` CSharp
// Prerequisite: DurableTaskClient is already configured.
// IServiceCollection
services.AddDurableClientAspNetCore(o => o.OperationLocation = new("GetStatus"));

// WebApplication
app.MapGet("/api/status/{instanceId}", (string instanceId) => Results.Extensions.OrchestrationStatus(instanceId)).WithName("GetStatus");
```

For more information, see https://github.com/microsoft/durabletask-dotnet
