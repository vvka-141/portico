# Portico

**The command surface for .NET backend services.**

Your service's operational surface is an API. Treat it like one.

Portico is contract-first CLI routing for .NET: your routes are routes, your examples are tests, and
Roslyn analyzers check both at compile time. Zero dependencies. DI is opt-in.

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
    Assert.True(example.Matched, $"Example did not dispatch: {example.Example}");
```

Rename a route, make an argument required — the example stops dispatching and the build goes red.

But dispatching is the floor, not the ceiling. Each example also reports **which handler it
reached** and **what values were bound to it**, so an example can pin the whole contract:

```csharp
var seed = new CliContractValidator<IAdminTool>().Enumerate()
    .Single(e => e.Example == "db seed --rows 100");

Assert.Equal(nameof(IAdminTool.Seed), seed.Handler);   // the route, pinned
Assert.Equal(100, seed.Arguments["rows"]);             // the binding, pinned — an int, not "100"
```

Retype `--rows` from `int` to `string` and the example still *dispatches* — but it no longer binds
`100`, and the build goes red. **The documentation cannot drift from the code, because the
documentation is the test.** The analyzer (`POR004`) fails the build if a route ships with no
example at all.

This is not a hypothetical. Writing the worked example in this repo, that test caught a real bug in
the framework on its first run — `TimeSpan?` was not accepting `"30 seconds"`. It is fixed. That is
exactly what the mechanism is for.

## Compile-time checks, not runtime surprises

Portico ships Roslyn analyzers **inside the package**. One `dotnet add package` and your build starts
checking your CLI:

| | |
|---|---|
| `POR001` | a `{placeholder}` in a route matches no parameter |
| `POR002` | two methods declare the same route |
| `POR003` | a malformed `[CliOption]` spec |
| `POR004` | a `[CliRoute]` with no `[CliCommandExample]` |
| `POR005` | `[CliArgument]` names a parameter that does not exist |
| `POR006` | a `CliOptions` bundle with no public parameterless constructor |
| `POR007` | one parameter targeted by two `[CliArgument]`s |
| `POR008` | a `[CliRoute]` method that cannot return an exit code |

## Secrets do not reach your logs

Mark an option `Sensitive = true` and its value is redacted wherever the framework echoes the command
line — trace output, timing output, conversion errors:

```
[timing] admin db migrate --connection-string *** ... 22 ms
```

And when a user mistypes a command, Portico prints the route they typed — **never the option
values**. No route matched, so it has no way to know which of them was a password. In a container,
stderr is the log stream; that is not a place to guess.

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

The core has **zero** dependencies, and a test asserts it. The `Microsoft.Extensions.*` adapters are
separate packages precisely so it stays that way. (The two adapter packages are in progress.)

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
  went 2.0 GA in November 2025 and is the safe institutional choice. Portico's bet is that its
  builder-and-lambda shape is the reason people leave it.
- **Not a REPL, not a DSL, not a config-file format.** One command, one invocation, one exit code.

## Known rough edges

Portico is **0.x**. It is extracted from a framework with ~380 tests behind it, but it is honest
about what is not finished:

- Top-level `--help` lists every command's options inline instead of a `Commands:` summary.

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
- [`examples/AdminCli`](examples/AdminCli) — a backend admin CLI (`migrate`, `seed`, `reindex`, `drain`, `health`), built and contract-tested by CI

## Licence

[Apache-2.0](LICENSE).
