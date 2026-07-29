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

**This is the reason to pick this package over `Portico.DependencyInjection`.** Graceful shutdown
becomes the host's: Ctrl+C and SIGTERM go through `IHostApplicationLifetime`, and Portico detects a
cancellable token and installs **no** signal handler of its own rather than racing the host's. Your
`CancellationToken` parameters are cancelled once, by one owner, and the command's exit code still
reaches `Main` — so `docker stop` drains instead of killing, and the shell sees 143.

**Depends on:** `Portico`, `Portico.DependencyInjection` (both pulled automatically),
and `Microsoft.Extensions.Hosting`.

**Full documentation:** [github.com/vvka-141/portico](https://github.com/vvka-141/portico) · **Issues and feedback:** [github.com/vvka-141/portico/issues](https://github.com/vvka-141/portico/issues)

Portico is 0.x. The API is still being shaped by what breaks, so a rough edge is worth an
issue rather than a workaround.
