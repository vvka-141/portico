# Portico.Hosting

Generic Host integration for [Portico](https://www.nuget.org/packages/Portico). Your service
already has a `HostApplicationBuilder`, its configuration, its logging and its container — its
admin CLI should reuse them, not rebuild them.

```
dotnet add package Portico.Hosting
```

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<IMigrator, Migrator>();
builder.Services.AddPorticoCommands<IAdminTool, AdminTool>();

return await builder.Build().RunPorticoAsync(args);
```

Graceful shutdown is the host's: Ctrl+C and SIGTERM go through `IHostApplicationLifetime`,
and Portico stands down rather than installing a second handler to race it. The command's
exit code reaches `Main`.

**Depends on:** `Portico`, `Portico.DependencyInjection` (both pulled automatically),
and `Microsoft.Extensions.Hosting`.

**Full documentation:** [github.com/vvka-141/portico](https://github.com/vvka-141/portico) · **Issues and feedback:** [github.com/vvka-141/portico/issues](https://github.com/vvka-141/portico/issues)

Portico is 0.x. The API is still being shaped by what breaks, so a rough edge is worth an
issue rather than a workaround.
