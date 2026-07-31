# Portico

**A contract-first operational command framework for .NET systems.** Compile the CLI with the
application and domain assemblies it operates, then expose their capabilities through ordinary C#
interfaces, attributes, and middleware. Portico discovers that contract at runtime by design; there
is no generated mirror of the command model to keep aligned.

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
and a stale one fails the test suite and CI.

The contract can live beside the capability it exposes. Derive domain-specific option attributes,
package command implementations with their application services, and compose several such
assemblies into one operational executable. Portico adds the argv boundary; it does not require a
second application architecture.

Or start from a template — one for a new project, one for a service you already have:

```
dotnet new install Portico.Templates

dotnet new portico-cli -n MyCli && cd MyCli && dotnet test   # a whole new CLI
dotnet new portico-command -n Migrate                        # one command, into this project
```

## Operational safeguards and binding

- **Secrets stay out of your logs.** `[CliOption("--token", Sensitive = true)]` — the value is
  withheld from every message the framework composes, including conversion errors and trace output.
- **Durations the way an operator types them.** `--timeout "30 seconds"`, `90s`, `1h30m`, `PT30S`.
  A bare `--timeout 30` is refused rather than silently meaning thirty **days**, which is what
  .NET's own `TimeSpan` parser does with it.
- **17 collection shapes**, immutable ones included — `ImmutableArray<T>`, `ImmutableHashSet<T>` and
  the rest bind directly. So do `string`-keyed maps: `--shard eu=3` (or `'--shard[eu]' 3`).
- **Named POSIX exit codes.** `CliExitException.UsageErrorExitCode`, `CancelledExitCode`,
  `TerminatedExitCode` — so a pipeline reading `$?` can tell *misconfigured* from *failed*.

[The whole surface](https://github.com/vvka-141/portico/blob/main/docs/reference/capabilities.md),
every entry backed by a test.

For a dated, source-checked comparison with other .NET CLI frameworks, including prior art and the
cases where another framework is the better choice, see
[The alternatives, honestly](https://github.com/vvka-141/portico/blob/main/docs/explanation/alternatives.md).

For the architectural case, trade-offs, and boundaries, see
[Why Portico?](https://github.com/vvka-141/portico/blob/main/docs/explanation/why-portico.md).

## Related packages

| Package | What it adds |
|---------|-------------|
| `Portico.DependencyInjection` | Resolve contracts from an `IServiceProvider`, one scope per command |
| `Portico.Hosting` | Generic Host integration — reuse your service's host, DI, config and logging |

**Full documentation:** [github.com/vvka-141/portico](https://github.com/vvka-141/portico) · **Issues and feedback:** [github.com/vvka-141/portico/issues](https://github.com/vvka-141/portico/issues)

Portico is 0.x. The API is still being shaped by what breaks, so a rough edge is worth an
issue rather than a workaround.
