# Capabilities

The surface, enumerated. Everything on this page is shipped, and **everything on this page is proved
by a test** — `test/Portico.Tests/CliCapabilities_Should.cs` exercises each capability end to end
through the real pipeline. If one of those goes red, this page is lying, and that is a bug.

That is not ceremony. This page's parent ticket originally claimed a capability the framework flatly
rejects — filed from a grep of a method name, without reading the method. A capability doc with no
executable proof is exactly how that reaches users.

## Options

### Environment-variable fallback

Config layering without a config file, declared on the option itself.

```csharp
[CliOption("--token", "API token", EnvironmentVariable = "PORTICO_API_TOKEN")] string? token = null
```

The command line wins over the environment; the environment wins over the default. An operator sets
`PORTICO_API_TOKEN` once in the container and stops typing it.

**Scalar options only, today.** `EnvironmentVariable` on a `CliFlag?`, a collection or a map is
**silently inert** — the option takes its default as though the variable were unset, with no
diagnostic. The fallback lives in the scalar materializer and nowhere else. If you are configuring a
containerized service from the environment, this is the edge you will hit; it is pinned by
`CliEnvironmentFallback_Should` and tracked as a bug rather than hidden.

### `DefaultValue` — the string form

```csharp
[CliOption("--rows", "How many rows", DefaultValue = "42")] int rows
```

Distinct from a C# default (`int rows = 42`), and useful when the parameter has none: the value is
parsed through the same converter a typed value would be, so it is written the way a *user* would
type it, not the way C# would.

### `Sensitive` — the value never reaches an echo of the command line

```csharp
[CliOption("--connection-string|-c", Sensitive = true)] string connectionString
```

The value is redacted (`***`) everywhere the framework echoes argv: trace output, timing output,
conversion errors. And when a command is mistyped, Portico prints the route and **no option values at
all** — no route matched, so it cannot know which of them was a password, and it does not guess.

**This is an agent-safety feature, and it is worth naming as one.** It was built to keep secrets out
of container logs, where stderr *is* the log stream. The same mechanism keeps them out of an agent's
transcript — an agent that runs your CLI and reads its output never sees the credential. That is a
free, shipped answer to a live concern.

### Map options — the `?cfg[env]=prod` analogue

```csharp
[CliOption("--shard", "Per-region shard counts")] Dictionary<string, int>? shard = null
```

```
admin reindex --shard[eu] 3 --shard[us] 5
```

First-class, not a parsing trick: the key is a string, the value is converted like any other option.

### `CliFlag?` versus `bool` — presence versus value

This distinction is easy to miss and worth stating plainly.

| Declaration | Meaning | Typed as |
|---|---|---|
| `CliFlag? dryRun` | **presence-only** — set by being there | `--dry-run` |
| `bool force` | a **two-state value** option | `--force true` / `--force false` |

`CliFlag?` is what you want for the ordinary `--verbose` / `--dry-run` switch. A `bool` reads a value,
so `--force` alone is not how it is used. Using `bool` where `CliFlag?` was meant is the most common
misuse in the framework.

### Human-readable durations

A `TimeSpan` binds the way an operator actually types one:

```
--timeout "30 seconds"    --timeout "5 min"     --timeout "1.5 hours"
--timeout PT30S           --timeout 00:00:30
```

All five bind. `TimeSpan?` behaves identically — which it did not, until a worked example caught it
(the bug is in the changelog).

### `CliOptions` bundles — the `[FromBody]` analogue

A group of options that travel together becomes a class:

```csharp
public sealed class ConnectionOptions : CliOptions
{
    [CliOption("--host")] public string Host { get; set; } = "localhost";
    [CliOption("--port")] public int Port { get; set; } = 5432;
}

[CliRoute("connect")]
int Connect(ConnectionOptions connection) => 0;
```

A bundle is constructed per invocation, so it needs a public parameterless constructor — analyzer
`POR006` enforces that. (`CliMiddleware` inherits from `CliOptions` but is *not* subject to it: you
construct middleware yourself, and a constructor dependency is exactly how a container injects into
it.)

## Routing

### Route ranking is a tie-breaker, not overload selection

When two routes match the same command line **with equal segment shapes**, Portico scores them by
which options are present: **+1** per matched option, **−1** per missing required option, **−1** per
unrecognized option. The higher score wins.

```csharp
[CliRoute("db migrate")] int Migrate([CliOption("--force")] CliFlag? force = null) => 0;
[CliRoute("db {command}")] int Passthrough(string command) => 0;

// `admin db migrate --force`  →  Migrate.
// --force is recognized by the literal route (+1) and unrecognized by the placeholder (−1).
```

**What this is not:** two methods cannot share a route signature and be selected between by their
options. That is a configuration error — the framework refuses it at `CliApplication.Create`, and
analyzer `POR002` catches it at build time. If you came here looking for ASP.NET's action-selector
semantics, they are not here.

### A literal route beside a catch-all is not a supported shape

Given `[CliRoute("db migrate")]` and `[CliRoute("db {command}")]`, the command `admin db migrate`
with no distinguishing option matches **both**, equally. Portico does not silently prefer the literal:

```
$ admin db migrate
The command line matches more than one command. Candidates:
  db migrate
  db {command}
Disambiguate by supplying additional options or by using a more specific subcommand.
exit 2
```

This is deliberate — explicit over implicit. ASP.NET and Express would quietly pick the literal;
Portico declines to guess and tells you why. It is a real constraint, and if you are modelling a
passthrough command, model it with a distinct prefix rather than a catch-all beside a literal.

### "Did you mean"

A mistyped route is met with the closest real ones, ranked by edit distance:

```
$ admin db migrat
Unknown command: admin db migrat.
Did you mean:
  db migrate
Run with --help for the full command list.
```

## The process

### Exit codes

`0` success, `1` runtime error, `2` usage error, `130` cancelled (SIGINT), `143` terminated
(SIGTERM). A handler returns an `int` or throws `CliExitException` with the code it wants; analyzer
`POR008` rejects a handler that cannot carry one.

### Cancellation, wired for you

Declare a `CancellationToken` parameter and it is honoured: Ctrl+C (SIGINT) cancels it and exits
**130**, and SIGTERM — what Docker and Kubernetes send before SIGKILL — cancels it and exits **143**.
Your `migrate` command drains instead of being killed mid-transaction.

Pass your own cancellable token (`RunAsync(args, token)`) and Portico installs **no** handlers of its
own, deferring to whatever owns the lifetime — that is exactly how
[`Portico.Hosting`](../../src/Portico.Hosting) hands over to the Generic Host.

### Shell completion

```csharp
app.EmitCompletion(CliCompletionShell.Bash, "admin", Console.Out);
```

Emits a self-contained bash or zsh completion script for the application's routes. Wire it to a
hidden command and `admin completion bash > /etc/bash_completion.d/admin`.

### Middleware — the `IActionFilter` analogue

`CliMiddleware` gets `OnExecutingAction` / `OnActionExecuted` / `OnError`, and can declare its own
options (`--verbose`, `--timing`) which become available to every command. `OnActionExecuted` runs
from a `finally`, so it is the symmetric partner of `OnExecutingAction` even when the command threw.

Middleware is constructed by you and cloned per dispatch, so it can take constructor dependencies —
`Portico.DependencyInjection` resolves them: `cfg.UseMiddleware<AuditMiddleware>(serviceProvider)`.

Two ship in the box: `CliTimingMiddleware` (`--timing`) and `CliTracingMiddleware`
(`--trace-level`).

## See also

- [Composing CLIs](../how-to/compose-clis.md) — mounting several contracts into one binary
- [Analyzer rules](analyzer-rules.md) — the ten compile-time checks
- [Extensibility](../explanation/extensibility.md) — what you can extend, and what is sealed
