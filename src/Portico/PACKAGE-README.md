# Portico

**The command surface for .NET backend services.** Contract-first CLI routing where **your
examples are executable tests** — one `CliContractValidator<T>` test runs every
`[CliCommandExample]` through the real pipeline, and Roslyn analyzers check the rest at compile
time. Zero dependencies.

```
dotnet add package Portico
```

```csharp
using Portico;

public interface IAdminTool
{
    [CliRoute("db migrate")]
    [CliCommandExample("db migrate --connection-string \"Host=db\"")]
    int Migrate([CliOption("--connection-string|-c", Sensitive = true)] string connectionString);
}
```

One `CliContractValidator<T>` test runs every `[CliCommandExample]` through the real pipeline,
and a stale one fails the build.

Or start from a template — one for a new project, one for a service you already have:

```
dotnet new install Portico.Templates

dotnet new portico-cli -n MyCli && cd MyCli && dotnet test   # a whole new CLI
dotnet new portico-command -n Migrate                        # one command, into this project
```

## Related packages

| Package | What it adds |
|---------|-------------|
| `Portico.DependencyInjection` | Resolve contracts from an `IServiceProvider`, one scope per command |
| `Portico.Hosting` | Generic Host integration — reuse your service's host, DI, config and logging |

**Full documentation:** [github.com/vvka-141/portico](https://github.com/vvka-141/portico) · **Issues and feedback:** [github.com/vvka-141/portico/issues](https://github.com/vvka-141/portico/issues)

Portico is 0.x. The API is still being shaped by what breaks, so a rough edge is worth an
issue rather than a workaround.
