# Migrating from Cocona

[Cocona](https://github.com/mayuki/Cocona) was archived by its author on **14 December 2025**
("This repository was archived by the owner on Dec 14, 2025. It is now read-only"). In the pinned
issue announcing it, mayuki recommends
[ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) as the successor.

Cocona was the ASP.NET-flavoured, methods-are-commands, attribute-binding .NET CLI framework — which
is Portico's shape too. If you are looking for somewhere to go, this page tells you how to move, and
just as importantly, **when not to**.

## Read this first: Portico is not always the right destination

- **If you chose Cocona and later wished for `Cocona.Lite` — the AOT/startup/binary-size path —
  go to [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework).** It is a source
  generator with zero reflection and NativeAOT support. Portico uses reflection deliberately and
  will lose that comparison; we are not going to pretend otherwise. See
  [why](../explanation/aot.md).
- **If you want the institutional default,
  [System.CommandLine](https://learn.microsoft.com/dotnet/standard/commandline/) went 2.0 GA** in
  November 2025. It is Microsoft's, and that matters in some organisations more than any API
  argument. Note that it GA'd with `System.CommandLine.Hosting` and
  `System.CommandLine.NamingConventionBinder` deprecated on NuGet — so if you came to Cocona *for*
  the attribute binding and the DI story, that is the part System.CommandLine does not currently
  give you.
- **Portico is the right destination if you chose Cocona for its shape**: an operational surface
  declared as attributed methods on a service, bound like an ASP.NET controller, injected with your
  container, testable. Plus the two things Cocona did not do: a **zero-dependency core**, and
  **examples that are executable tests**.

## What actually differs

| Cocona | Portico |
|---|---|
| `[Command("db migrate")]` on a method | `[CliRoute("db migrate")]` on a method |
| `[Option('n', Description = "...")] string name` | `[CliOption("--name\|-n", "...")] string name` |
| `[Argument] string path` | a `{path}` placeholder in the route, optionally described: `[CliRoute("cmd {path}")] int Cmd([CliArgument("...")] string path)` |
| a `bool` flag parameter | `CliFlag?` (a flag is a distinct type, not a `bool` that could also be an option) |
| `ICommandParameterSet` | a `CliOptions` bundle |
| `CommandFilterAttribute` / `ICommandFilter` | `CliMiddleware` |
| `CoconaAppContext.CancellationToken` | a `CancellationToken` parameter on the method |
| `CoconaApp.Run<Program>(args)` | `CliApplication.Create(cfg => cfg.AddCommands(new Tool())).Run(args)` |
| `CoconaApp.CreateBuilder()` + `builder.Services` | `Portico.DependencyInjection` (opt-in adapter package) |
| `Cocona.Lite` — to escape `Microsoft.Extensions.*` | not needed: **the core package has no dependencies at all** |
| — | `[CliCommandExample]` + `CliContractValidator<T>`: every example is a test |

The dependency line is the one worth dwelling on, because it is the complaint Cocona users filed
most. Cocona's core pulled `Microsoft.Extensions.DependencyInjection`, `.Hosting` and `.Logging`,
which is why `Cocona.Lite` had to exist. Portico inverts that: the core has **zero** dependencies and
a test asserts it; DI and the Generic Host arrive as separate opt-in packages
(`Portico.DependencyInjection`, `Portico.Hosting`) if you want them. There is no "lite" variant to
choose between, because there is nothing to escape.

## Before and after

A Cocona service:

```csharp
using Cocona;

public class AdminTool
{
    [Command("db migrate", Description = "Apply pending database migrations.")]
    public async Task<int> MigrateAsync(
        [Option('c', Description = "Postgres connection string")] string connectionString,
        [Option(Description = "Print the plan; change nothing")] bool dryRun,
        CoconaAppContext ctx)
    {
        // ... ctx.CancellationToken
        return 0;
    }

    [Command("db seed", Description = "Seed reference data.")]
    public int Seed([Option(Description = "How many rows to seed")] int rows = 10) => 0;
}

class Program
{
    static void Main(string[] args) => CoconaApp.Run<AdminTool>(args);
}
```

The same surface in Portico. The contract below is lifted verbatim from
[`examples/AdminCli/IAdminTool.cs`](../../examples/AdminCli/IAdminTool.cs) — a project CI builds, and
whose every example CI runs through the real pipeline, on every push. It compiles because it is
compiled:

```csharp
using Portico;

public interface IAdminTool
{
    /// <summary>Apply pending database migrations.</summary>
    [CliRoute("db migrate")]
    [CliCommandExample("db migrate --connection-string \"Host=db;Username=svc\"")]
    [CliCommandExample("db migrate --connection-string \"Host=db\" --dry-run")]
    Task<int> MigrateAsync(
        [CliOption("--connection-string|-c", "Postgres connection string", Sensitive = true)]
        string connectionString,
        [CliOption("--dry-run", "Print the plan; change nothing")] CliFlag? dryRun = null,
        CancellationToken cancellation = default);

    /// <summary>Seed reference data.</summary>
    [CliRoute("db seed")]
    [CliCommandExample("db seed --rows 100")]
    [CliCommandExample("db seed")]
    int Seed([CliOption("--rows", "How many rows to seed")] int rows = 10);
}

public static class Program
{
    public static int Main(string[] args) =>
        CliApplication.Create(cfg => cfg.AddCommands(new AdminTool())).Run(args);
}
```

Four differences are doing real work here, and they are the reason to move rather than just a
rename:

1. **The contract is an interface.** That is what lets `CliContractValidator<IAdminTool>` run every
   example through the real pipeline against a `DispatchProxy` — see below.
2. **`[CliCommandExample]` is not a comment.** Each one is executed by your test suite. The `POR004`
   analyzer fails the build for a route that ships without one.
3. **`Sensitive = true`** redacts the value everywhere the framework echoes the command line. A
   connection string does not reach your logs.
4. **`CliFlag?` instead of `bool`.** A flag is its own type, so "absent" and "false" stop being the
   same thing.

## The migration is mechanical, and then it is not

Renaming the attributes takes an afternoon. The part worth budgeting for is the one that has no
Cocona equivalent: turning your commands into a contract you can verify.

```csharp
public static IEnumerable<object[]> Examples() =>
    new CliContractValidator<IAdminTool>().Enumerate().Select(e => new object[] { e });

[Theory]
[MemberData(nameof(Examples))]
public void Dispatch(CliContractExample example) =>
    Assert.True(example.Matched, $"Example did not dispatch: {example.Example}");
```

Every `[CliCommandExample]` becomes a test case. Rename a route, make an argument required, retype an
option — the example stops dispatching, or stops binding the value it used to bind, and the build
goes red. Each example also reports **which handler it reached** and **what was bound to it**, so it
pins the whole contract and not just routability.

That mechanism is the reason to pick Portico over a straight rename to something else. It is also,
honestly, the only thing here that Cocona could not have done for you.

## Things Portico does not have

- **No AOT, no `Lite` variant.** Reflection is deliberate. If that is your constraint,
  ConsoleAppFramework is the better answer.
- **`dotnet new portico-cli`** scaffolds a project with one route, one example and a green contract
  test — but the template is young and not a full migration target yet.
- **No shell-completion parity claims** beyond what is in the box (`CliCompletion` emits bash, zsh
  and PowerShell scripts).
- **Portico is 0.x.** SemVer's 0.x licence — anything may change — is the preview channel. There is
  no user base to keep compatible with yet, and we will tell you what changed in the
  [changelog](../../CHANGELOG.md).
