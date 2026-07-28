# Portico, for coding agents

Portico builds a .NET CLI from a **contract**: a plain C# interface whose methods are
routes. This file is the whole API. Read it before writing Portico code — the framework
has near-zero training-data presence, so your priors about it are probably wrong.

Zero dependencies. Targets `net8.0` and `net10.0`. `using Portico;`

## The one rule that catches everyone

**The `[CliRoute]` string is the command's entire path.** A `{placeholder}` in it is a
positional argument, bound to the method parameter of the same name. Everything else is an
option. There is no other way to add a path segment — attribute order and parameter order
never affect the route.

```csharp
[CliRoute("worker {id} drain")]      // command:  mytool worker w-42 drain
int Drain(string id) => 0;           // {id} binds to 'id'
```

## Handler contract

A route is a method that returns `int` or `Task<int>` (the exit code: `0` ok, `1` runtime
error, `2` usage error, `130` cancelled). Use `Console.Write*` for output — the framework
owns routing and binding, not what you print. Throw `CliExitException` to fail with a code.

## The attributes

| Attribute | Role | HTTP analogue |
|---|---|---|
| `[CliRoute("a b {x}")]` | the command path (method, or a prefix on the class/interface) | `[Route("a/b/{x}")]` |
| `[CliArgument("desc")]` on a parameter | describes a `{placeholder}` — **never adds one** | `[FromRoute]` |
| `[CliOption("--name\|-n", "desc")]` | a named option | `[FromQuery]` |
| `CliFlag?` parameter type | a presence-only flag | — |
| `CliOptions` subclass parameter | a bundle of related options | `[FromBody]` |
| `[CliCommandExample("...")]` | an executable example (**required** on every route) | integration test |
| `CliMiddleware` | before/after/error hooks + global options | `IActionFilter` |

## Options: the parts agents get wrong

**`CliFlag?` is presence-only; `bool` is two-state.** This is the single most common mistake.

```csharp
[CliOption("--verbose|-v")] CliFlag? verbose   // absent = off; `--verbose` or `-v` = on. No value.
[CliOption("--force")]      bool force          // needs a value: `--force true`. Bare `--force` errors.
```

**Map option** — the `?keep[topic]=7` of a CLI, binds to `Dictionary<K,V>`:

```csharp
[CliOption("--keep")] Dictionary<string,int>? keep   //  '--keep[orders]' 7 '--keep[audit]' 90
```

**Collection** — repeat the flag; binds `List<T>`, `T[]`, `IReadOnlyList<T>`, `HashSet<T>`, …:

```csharp
[CliOption("--status")] List<string>? status         //  --status idle --status draining
```

**Defaults.** A method-parameter default (`int rows = 10`) makes an option optional. A
`CliOptions` **bundle property** does *not* work that way — give it `DefaultValue = "10"` on
the attribute, or it is required. `TimeSpan` reads how an operator types it: `"30 seconds"`,
`"2 minutes"`, `"1.5 hours"`, `"PT2M"`. `EnvironmentVariable = "VAR"` supplies a fallback
(command line > environment > default). `Sensitive = true` redacts the value from every log
line Portico emits.

## Examples are tests — the point of the framework

Every `[CliCommandExample]` is run through the real pipeline by `CliContractValidator<T>`
and the build fails if it stops dispatching. The example is the contract, not a comment. The
analyzers (below) enforce that a route without an example fails the build.

## A complete, working tool

Contract + implementation + test. This compiles, dispatches, and is contract-tested.

```csharp
using Portico;
using System.Threading.Tasks;

public interface IGreeter
{
    [CliRoute("greet {name}")]
    [CliCommandExample("greet Ada")]
    [CliCommandExample("greet Ada --shout")]
    int Greet(string name, [CliOption("--shout")] CliFlag? shout = null);
}

public sealed class Greeter : IGreeter
{
    public int Greet(string name, CliFlag? shout)
    {
        var msg = $"Hello, {name}";
        System.Console.WriteLine(shout is null ? msg : msg.ToUpperInvariant() + "!");
        return 0;
    }
}

// Program.cs
public static class Program
{
    public static int Main(string[] args) =>
        CliApplication.Create(cfg => cfg.AddCommands(new Greeter())).Run(args);
}
```

```csharp
// GreeterContract_Should.cs  (xUnit)
using System.Collections.Generic;
using System.Linq;
using Portico.Testing;
using Xunit;

public sealed class GreeterContract_Should
{
    public static IEnumerable<object[]> Examples() =>
        new CliContractValidator<IGreeter>().Enumerate().Select(e => new object[] { e });

    [Theory, MemberData(nameof(Examples))]
    public void Dispatch(CliContractExample e) =>
        Assert.True(e.Matched, $"{e.Example} did not dispatch: {e.FailureReason}");

    [Fact]
    public void Bind_The_Name()
    {
        var e = new CliContractValidator<IGreeter>().Enumerate().Single(x => x.Example == "greet Ada");
        Assert.Equal(nameof(IGreeter.Greet), e.Handler);
        Assert.Equal("Ada", e.Arguments["name"]);   // handler AND bound value, pinned
    }
}
```

## Bundles, validation, cancellation, DI

- **Bundle:** a `sealed class : CliOptions` with `[CliOption]` properties (needs a public
  parameterless ctor — analyzer POR006). Implement `IValidatableObject` for cross-property
  rules; a failure exits 2 before the handler runs.
- **Cancellation:** declare a `CancellationToken` parameter — the framework injects the
  ambient one (trips to exit 130 on Ctrl+C under `RunAsync`).
- **DI:** `AddCommands(() => factory())` runs the factory per invocation — return a
  container-resolved instance. `CliMiddleware` takes constructor dependencies directly:
  `UseMiddleware(new AuditMiddleware(sink))`. The core has no container; the
  `Portico.DependencyInjection` package bridges `Microsoft.Extensions.DependencyInjection`.

## Composition

Mount a second contract under a literal prefix — it never has to know the prefix:

```csharp
cfg.AddCommands(new DiagnosticsTool(), [new CliRouteAttribute("diag")]);  //  mytool diag health
```

A mount prefix is **literal only** (it applies to commands declared elsewhere). A type-level
`[CliRoute]` prefix *can* carry a `{placeholder}` — it decorates your own methods.

## The analyzers — your edit-loop verifier

These fire at build time (and in the IDE). ConsoleAppFramework, CliFx, and DotMake.CommandLine also
ship compile-time diagnostics; Portico's rules cover different ground because they follow from the
attribute-routing and contract-validation model. If you see one, the message names the fix.

| ID | Fires when |
|---|---|
| POR001 | a route `{placeholder}` matches no parameter |
| POR002 | two methods on one type declare the same route |
| POR003 | a `[CliOption]` spec is malformed (need `--long` or `-s`, pipe-separated) |
| POR004 | a `[CliRoute]` method has no `[CliCommandExample]` |
| POR005 | a `[CliArgument]` parameter has no matching `{placeholder}` |
| POR006 | a `CliOptions` bundle has no public parameterless constructor |
| POR008 | a `[CliRoute]` method does not return `int`/`Task<int>` |
| POR009 | two options on one command share an alias |
| POR010 | a `[CliOption]` type cannot be built from a command-line string |
| POR011 | a route repeats a `{placeholder}` name (the second slot would overwrite the first) |

## Worked reference

The `examples/ReferenceCli` project in the Portico repository is a full-surface, contract-
tested CLI (`fleet`) — map options, bundles with validation, `RankByOptions`, middleware
with DI, composition. It is the ground truth for correct Portico code.
