# Portico

**The command surface for .NET backend services.** Contract-first CLI routing where your
examples are tests, verified at compile time by Roslyn analyzers. Zero dependencies.

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

Or start from the template:

```
dotnet new install Portico.Templates
dotnet new portico-cli -n MyCli && cd MyCli && dotnet test
```

**Full documentation:** [github.com/vvka-141/portico](https://github.com/vvka-141/portico)

## Related packages

| Package | What it adds |
|---------|-------------|
| `Portico.DependencyInjection` | Resolve contracts from an `IServiceProvider`, one scope per command |
| `Portico.Hosting` | Generic Host integration — reuse your service's host, DI, config and logging |
