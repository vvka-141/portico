# Portico.DependencyInjection

`Microsoft.Extensions.DependencyInjection` adapter for [Portico](https://www.nuget.org/packages/Portico).
Resolve your CLI command contracts from an `IServiceProvider`, one scope per dispatched command.

```
dotnet add package Portico.DependencyInjection
```

```csharp
var services = new ServiceCollection()
    .AddScoped<IAdminTool, AdminTool>()
    .AddScoped<IDbConnection>(_ => new NpgsqlConnection(cs))
    .BuildServiceProvider();

CliApplication.Create(cfg => cfg.AddCommands<IAdminTool>(services)).Run(args);
```

Each dispatched command gets its own `IServiceScope`, disposed when the command finishes —
whether it succeeded, threw, or was cancelled. `AddScoped` means what it means.

The factory stays lazy: a `health` command never constructs the connection pool
a `migrate` command needs.

**Depends on:** `Portico` (pulled automatically) and
`Microsoft.Extensions.DependencyInjection.Abstractions`.

**Full documentation:** [github.com/vvka-141/portico](https://github.com/vvka-141/portico) · **Issues and feedback:** [github.com/vvka-141/portico/issues](https://github.com/vvka-141/portico/issues)

Portico is 0.x. The API is still being shaped by what breaks, so a rough edge is worth an
issue rather than a workaround.
