# Portico.DependencyInjection

`Microsoft.Extensions.DependencyInjection` adapter for [Portico](https://www.nuget.org/packages/Portico).
Resolve your CLI command contracts from an `IServiceProvider`, one scope per dispatched command. It
lets the operational boundary use the same implementations and dependency graph as the rest of the
.NET system instead of rebuilding them behind CLI-specific factories.

```
dotnet add package Portico.DependencyInjection
```

```csharp
using Portico;
using Portico.DependencyInjection;   // the IServiceProvider overload lives here

// `services` is the IServiceProvider your service already builds.
CliApplication.Create(cfg => cfg.AddCommands<IAdminTool>(services)).Run(args);
```

Register your contract in that container as you would anything else —
`services.AddScoped<IAdminTool, AdminTool>()` — and its dependencies alongside it.

> Building a container from scratch rather than reusing one? `BuildServiceProvider()` is in
> **`Microsoft.Extensions.DependencyInjection`**, which this package does not depend on and you
> would add yourself. Depending only on `.Abstractions` is deliberate: it is what lets an adapter
> sit next to a container you already have without opinions about which one.

Each dispatched command gets its own `IServiceScope`, disposed when the command finishes —
whether it succeeded, threw, or was cancelled. `AddScoped` means what it means.

The factory stays lazy: a `health` command never constructs the connection pool
a `migrate` command needs.

**Depends on:** `Portico` (pulled automatically) and
`Microsoft.Extensions.DependencyInjection.Abstractions`.

**Full documentation:** [github.com/vvka-141/portico](https://github.com/vvka-141/portico) · **Issues and feedback:** [github.com/vvka-141/portico/issues](https://github.com/vvka-141/portico/issues)

Portico is 0.x. The API is still being shaped by what breaks, so a rough edge is worth an
issue rather than a workaround.
