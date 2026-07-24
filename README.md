# Portico

**The command surface for .NET backend services.**

Your service's operational surface is an API. Treat it like one.

ASP.NET Core for the terminal: your routes are routes, and **your examples are executable tests** —
so the CLI cannot lie about what it accepts. One `CliContractValidator<T>` test runs every
`[CliCommandExample]` through the real pipeline, and a stale one fails the build. Roslyn analyzers
check the rest at compile time. Zero dependencies. DI is opt-in.

```csharp
using Portico;

public interface IAdminTool
{
    [CliRoute("db migrate")]
    [CliCommandExample("db migrate --connection-string \"Host=db\"")]
    int Migrate([CliOption("--connection-string|-c", Sensitive = true)] string connectionString);
}

public sealed class AdminTool : IAdminTool
{
    public int Migrate(string connectionString)
    {
        System.Console.WriteLine("applied 3 migrations.");
        return 0;
    }
}

public static class Program
{
    public static int Main(string[] args) =>
        CliApplication.Create(cfg => cfg.AddCommands(new AdminTool())).Run(args);
}
```

That is the whole framework: a plain C# method, one route attribute, one example.

```
dotnet add package Portico
```

Or start from the template — a runnable CLI whose contract test is already green:

```
dotnet new install Portico.Templates
dotnet new portico-cli -n MyCli && cd MyCli && dotnet test
```

---

## Your examples are tests

`[CliCommandExample]` is not a comment. `CliContractValidator<T>` runs every example through the real
pipeline against a `DispatchProxy` of your interface — one test case per example:

```csharp
public static IEnumerable<object[]> Examples() =>
    new CliContractValidator<IAdminTool>().Enumerate().Select(e => new object[] { e });

[Theory]
[MemberData(nameof(Examples))]
public void Dispatch(CliContractExample example) =>
    Assert.True(example.Matched,
        $"Example did not dispatch: {example.Example}\n  Reason: {example.FailureReason}");
```

Rename a route, make an argument required — the example stops dispatching and the build goes red,
and it tells you why in the framework's own words:

```
Example did not dispatch: pay --amount abc
  Reason: Value 'abc' for option '--amount' is invalid. abc is not a valid value for Decimal.
```

But dispatching is the floor, not the ceiling. Each example also reports **which handler it
reached** and **what values were bound to it**, so an example can pin the whole contract:

```csharp
var seed = new CliContractValidator<IAdminTool>().Enumerate()
    .Single(e => e.Example == "db seed --rows 100");

Assert.Equal(nameof(IAdminTool.Seed), seed.Handler);   // the route, pinned
Assert.Equal(100, seed.Arguments["rows"]);             // the binding, pinned — an int, not "100"
```

Retype `--rows` from `int` to `string` and the example still *dispatches* — but it no longer binds
`100`, and the test above goes red. **The documentation stops drifting from the code, because the
documentation is the test.**

Two enforcement points, and it is worth being exact about which does what. `POR004` is an **Error**,
so a route that ships with no example at all breaks the build outright — no configuration, nothing to
opt into. Whether the examples' *contents* still dispatch is checked by the `CliContractValidator<T>`
test above; `dotnet new portico-cli` writes it for you, and a project laid out by hand needs that one
test for the guarantee to hold.

Compare what an example is everywhere else. `cobra.Command.Example`, oclif's `examples`, yargs'
`.example()`, OpenCLI's `examples: [string]` — free text, printed in help, checked by nobody. They are
correct on the day they are written and unverifiable ever after.

The exception, and we will name it: **Azure CLI's `azdev linter` genuinely runs help examples through
the real parser** and fails CI on a bad one. It is real prior art. The difference is scope — Microsoft
built it for one CLI, and it checks that an example's options are *recognised*; Portico makes it the
framework's central abstraction, and checks that an example *dispatches to a handler and binds
values*. More on [who got there first](docs/explanation/alternatives.md#who-got-there-first).

This is not a hypothetical. Writing the worked example in this repo, that test caught a real bug in
the framework on its first run — `TimeSpan?` was not accepting `"30 seconds"`. It is fixed. That is
exactly what the mechanism is for.

## Compile-time checks, not runtime surprises

Portico ships Roslyn analyzers **inside the package**. One `dotnet add package` and your build starts
checking your CLI:

| | |
|---|---|
| `POR001` | a `{placeholder}` in a route matches no parameter |
| `POR002` | two methods on one type declare the same route |
| `POR003` | a malformed `[CliOption]` spec |
| `POR004` | a `[CliRoute]` with no `[CliCommandExample]` |
| `POR005` | `[CliArgument]` has no matching route placeholder |
| `POR006` | a `CliOptions` bundle with no public parameterless constructor |
| `POR007` | one parameter carrying two `[CliArgument]`s |
| `POR008` | a `[CliRoute]` method that cannot return an exit code |
| `POR009` | two options on one command declaring the same alias |
| `POR010` | a `[CliOption]` whose type cannot be built from a command-line string |

Stated precisely, because a vague boast is worse than none: **no other .NET CLI framework reports
compile-time diagnostics for CLI attribute misuse.** Not "no competitor ships analyzers" — that would
be false, and you would catch it. [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework)
*is* an analyzer, a source generator with no DLL reference at all. What it does not do is diagnose your
CLI declarations: its validation runs at runtime, after binding, via `DataAnnotations`. The others
validate at runtime or not at all. ([The alternatives, honestly](docs/explanation/alternatives.md) —
with the versions and the date they were checked.)

## Secrets do not reach your logs

Mark an option `Sensitive = true` and its value is redacted wherever the framework echoes the command
line — trace output, timing output, conversion errors:

```
[timing] admin db migrate --connection-string *** ... 22 ms
```

And when a user mistypes a command, Portico prints the route they typed — **never the option
values**. No route matched, so it has no way to know which of them was a password. In a container,
stderr is the log stream; that is not a place to guess.

The same mechanism does a second job nobody asked it to: an agent that runs your CLI and reads its
output never sees the credential either. It was built for logs; it works for transcripts.

## Several teams, one binary

Each team ships its own contract — own routes, own examples, own tests. The platform team mounts them:

```csharp
CliApplication.Create(cfg => cfg
    .AddCommands(new StorageTool(), [new CliRouteAttribute("storage")])
    .AddCommands(new QueueTool(),   [new CliRouteAttribute("queue")]))
    .Run(args);
```

```
platform storage status --bucket invoices
platform queue   status --queue orders
```

Both tools declare a route called `status`. Neither team had to know: the mount point disambiguates,
not the route name. Composition itself is not novel — oclif and cobra have done it for years. What is
unusual is that verification survives the mount: tell the validator where a contract hangs
(`new CliContractValidator<IStorageTool>("storage")`) and every example is still executable, still
run against the composed route table, still red when it stops dispatching.

Sub-CLIs are .NET assemblies you reference — this is not a wrapper over the real `aws` or `az`, and
there is no runtime plugin discovery. See [composing CLIs](docs/how-to/compose-clis.md) and the
worked example in [`examples/PlatformCli`](examples/PlatformCli).

## It is an HTTP API without the H

| ASP.NET Core | Portico |
|---|---|
| `[Route("api/projects/{id}")]` | `[CliRoute("projects get {id}")]` |
| `[FromQuery] string format` | `[CliOption("--format")]` |
| `?cfg[env]=prod` | `--cfg[env] prod` (map options are first-class) |
| `[FromBody] RequestDto` | `CliOptions` bundle |
| `IActionFilter` | `CliMiddleware` |
| Integration tests | `CliContractValidator<T>` |

If a feature is hard to explain through that analogy, it does not belong in Portico.

## Packages

| Package | Contents | Dependencies |
|---|---|---|
| `Portico` | framework + analyzers + `Portico.Testing` | **none** |
| `Portico.DependencyInjection` | `IServiceProvider` adapter | `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `Portico.Hosting` | Generic Host integration | `Microsoft.Extensions.Hosting` |

The core has **zero** dependencies, and a test asserts it against the packed `.nupkg`. The
`Microsoft.Extensions.*` adapters are separate packages precisely so it stays that way.

DI is one extension method, and the factory stays lazy — a `health` command never constructs the
connection pool a `migrate` command needs:

```csharp
var services = new ServiceCollection()
    .AddScoped<IAdminTool, AdminTool>()
    .AddScoped<IDbConnection>(_ => new NpgsqlConnection(cs))
    .BuildServiceProvider();

CliApplication.Create(cfg => cfg.AddCommands<IAdminTool>(services)).Run(args);
```

Each dispatched command gets its own `IServiceScope`, disposed when the command finishes — whether it
succeeded, threw, or was cancelled. `AddScoped` means what it means.

Your service already has a host. Its admin CLI should reuse it, not rebuild it:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<IMigrator, Migrator>();
builder.Services.AddPorticoCommands<IAdminTool, AdminTool>();

return await builder.Build().RunPorticoAsync(args);   // the command's exit code, returned from Main
```

Graceful shutdown is the host's: Ctrl+C and SIGTERM go through `IHostApplicationLifetime`, and
Portico stands down rather than installing a second handler to race it.

## What Portico is not

Honest concessions, because a comparison that concedes nothing is not worth reading:

- **Not a rendering library.** [Spectre.Console](https://spectreconsole.net/) owns terminal
  rendering — tables, progress bars, colour. Compose with it; Portico stays out of its way.
- **Not the fastest.** [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) is a
  source generator with zero reflection, zero allocation and NativeAOT support. If startup time or
  binary size is your constraint, use it — it will beat Portico and we are not going to pretend
  otherwise.
- **Not AOT.** Portico uses reflection for routing, binding and help. This follows from the target:
  an admin CLI inside a service container does not care about a 36 ms startup delta. See
  [docs/explanation/aot.md](docs/explanation/aot.md) — the decision, and the conditions under which
  we would revisit it.
- **Not Microsoft's.** [System.CommandLine](https://learn.microsoft.com/dotnet/standard/commandline/)
  went **2.0 GA in November 2025** — the "perpetual beta" jab is dead, and 2.0 also cut library size
  ~32% and improved parsing ~40%. If your organisation's rule is "prefer the first-party option,"
  that rule is defensible. Portico's bet against it is not stability and not speed; it is **shape**.
  People who left it say they wanted *"a simple programming style rather than the complex fluent
  style with nested lambdas that the library favored"*
  ([discussion](https://github.com/spectreconsole/spectre.console/discussions/1397)) — which is
  a citation, not our taste, and it is why routes here are attributes on methods.
- **Not a REPL, not a DSL, not a config-file format.** One command, one invocation, one exit code.

The full comparison, with versions and the date they were checked:
[The alternatives, honestly](docs/explanation/alternatives.md).

[Cocona](https://github.com/mayuki/Cocona) — the framework closest to Portico's shape — was archived
by its author on 14 December 2025. If you are coming from it, there is a
[migration guide](docs/how-to/migrate-from-cocona.md), and it is explicit about when
ConsoleAppFramework or System.CommandLine is the better destination instead.

## Known rough edges

Portico is **0.x**. It is extracted from a framework with ~530 tests behind it, and it is honest
about what is not finished:

- **No machine-readable command manifest yet.** An agent learns the surface by reading `--help`,
  which is honest and verified but not structured.
- **A literal route beside a catch-all is not a supported shape.** `db migrate` alongside
  `db {command}` is ambiguous, and Portico refuses to guess rather than silently preferring the
  literal. Deliberate, and [documented](docs/reference/capabilities.md#a-literal-route-beside-a-catch-all-is-not-a-supported-shape).
- **No AOT.** Reflection is deliberate; see [the decision](docs/explanation/aot.md).

These are tracked and fixed in the open. If you hit something else, open an issue.

## Stability

Portico is **0.x**, and SemVer's 0.x licence — *anything may change* — **is** the preview channel.
There are no alpha/beta feeds. Breaking changes land in minor versions and are called out in the
changelog. **1.0 is cut when the API is one we would defend**, not when the code is done.

## Documentation

- [Charter](docs/explanation/charter.md) — the design constitution, and the invariants it will not trade
- [Extensibility](docs/explanation/extensibility.md) — what you can extend, and what is deliberately sealed
- [AOT](docs/explanation/aot.md) — why not, and what would change our mind
- [Roadmap](docs/ROADMAP.md) — the open decision, and the parked list
- [Capabilities](docs/reference/capabilities.md) — the whole surface, every entry backed by a test
- [The agent-first CLI contract, scored](docs/explanation/agent-first-contract.md) — what Portico answers, what it declines, and why
- [Analyzer rules](docs/reference/analyzer-rules.md) — the ten compile-time checks, and how to suppress one
- [The alternatives, honestly](docs/explanation/alternatives.md) — what every competitor is better at, and the one claim we make
- [Composing CLIs](docs/how-to/compose-clis.md) — mounting several contracts into one binary, and what that does not give you
- [Migrating from Cocona](docs/how-to/migrate-from-cocona.md) — the concept mapping, and when another framework is the better move
- [`examples/AdminCli`](examples/AdminCli) — a backend admin CLI (`migrate`, `seed`, `reindex`, `drain`, `health`), built and contract-tested by CI
- [`examples/PlatformCli`](examples/PlatformCli) — a master CLI over two independently-built tools, its composed surface contract-tested by CI

## Contributing

[CONTRIBUTING.md](CONTRIBUTING.md) — how to build it, and the four rules that are not negotiable.
Found a security problem? [SECURITY.md](SECURITY.md) — privately, never a public issue.
This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md).

## Licence

[Apache-2.0](LICENSE).
