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

## Four things nothing else in .NET does

A survey of six .NET CLI frameworks on 2026-07-29, verified against their source, found none of
these anywhere:

- **Secrets stay out of your logs.** `[CliOption("--token", Sensitive = true)]` — the value is
  withheld from every message the framework composes. All six others interpolate what the user typed
  into parse errors, and in a container stderr *is* the log stream.
- **Durations the way an operator types them.** `--timeout "30 seconds"`, `90s`, `1h30m`, `PT30S`.
  A bare `--timeout 30` is refused rather than silently meaning thirty **days**, which is what
  .NET's own `TimeSpan` parser does with it.
- **17 collection shapes**, immutable ones included — `ImmutableArray<T>`, `ImmutableHashSet<T>` and
  the rest bind directly. So do `string`-keyed maps: `--shard eu=3` (or `'--shard[eu]' 3`).
- **Named POSIX exit codes.** `CliExitException.UsageErrorExitCode`, `CancelledExitCode`,
  `TerminatedExitCode` — so a pipeline reading `$?` can tell *misconfigured* from *failed*.

[The whole surface](https://github.com/vvka-141/portico/blob/main/docs/reference/capabilities.md),
every entry backed by a test.

## Related packages

| Package | What it adds |
|---------|-------------|
| `Portico.DependencyInjection` | Resolve contracts from an `IServiceProvider`, one scope per command |
| `Portico.Hosting` | Generic Host integration — reuse your service's host, DI, config and logging |

**Full documentation:** [github.com/vvka-141/portico](https://github.com/vvka-141/portico) · **Issues and feedback:** [github.com/vvka-141/portico/issues](https://github.com/vvka-141/portico/issues)

Portico is 0.x. The API is still being shaped by what breaks, so a rough edge is worth an
issue rather than a workaround.
