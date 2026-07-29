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

**`--help` names the variable**, because for a containerized service `--help` is frequently the only
surface an operator has — they did not write the tool and have no checkout:

```
Options:
  --token             API token  (env: PORTICO_API_TOKEN)
  --host              Target host  (default: localhost)  (env: APP_HOST)
```

**The name, never the value.** Rendering the *value* is the leak that
[dotnet/command-line-api#1191](https://github.com/dotnet/command-line-api/issues/1191) raised in 2021
and nobody fixed. Portico never reads the variable on the help path, for a `Sensitive` option or any
other — a variable nobody marked sensitive can still hold something its author did not anticipate.
The variable's *name* is a declaration in source, safe to print, and exactly what the operator needs.
A `Sensitive` option shows its variable name and still renders its default as `***`.

Only options that declare a variable get the suffix, and **map options never do** — they reject
`EnvironmentVariable` at startup (see the table below), so there is nothing for help to show.

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
admin reindex '--shard[eu]' 3 '--shard[us]' 5
```

> **Shell quoting required.** The brackets in `--shard[eu]` are filename-expansion
> characters. On **zsh** (the default shell on macOS) an unquoted `--shard[eu]`
> fails with `zsh: no matches found` before Portico ever sees it. **Quote the
> option name** — `'--shard[eu]'` or `--shard\[eu\]` — in any shell invocation.
> Inside `[CliCommandExample]` attributes the value is an argv array and must
> *not* be quoted.

First-class, not a parsing trick: the key is a string, the value is converted like any other option.

**A repeated key accumulates when the value type is a collection.** Headers, labels and selectors
repeat keys as a matter of course, so the declared value type chooses the semantics:

```csharp
[CliOption("--header")] Dictionary<string, string>?   one    // repeated key → usage error
[CliOption("--header")] Dictionary<string, string[]>? many   // repeated key → accumulates
```

```
admin call '--header[Accept]' json html            one option, several values
admin call '--header[Accept]' json '--header[Accept]' html   the option repeated
```

Both forms bind the same thing. Key order and value order are preserved as typed, and each value
converts through the same path a collection option would — so `Dictionary<string,int[]>` reports a
bad element as a usage error naming the key.

This follows the query-string metaphor the feature comes from: `?tag=a&tag=b` is canonical, and it is
the *single*-value restriction that has no expression in it. A `Dictionary<string,T>` still rejects a
repeated key, so nothing silently became last-wins.

The value-collection rule is independent of the container: any map container below combined with any
[collection shape](#collection-options--many-values-or-a-repeated-option) as its value accumulates.
`ILookup<K,V>` is **not** supported — it has no public constructor — and is refused at
`CliApplication.Create` rather than failing at dispatch.

The key must be `string` — it is the text between the brackets. The declaration may be any of
`Dictionary<string,V>`, `IDictionary<string,V>`, `IReadOnlyDictionary<string,V>`,
`SortedDictionary<string,V>`, `ImmutableDictionary<string,V>`, `IImmutableDictionary<string,V>` or
`ImmutableSortedDictionary<string,V>`. Anything else map-shaped — `SortedList<,>`,
`ConcurrentDictionary<,>`, a non-string key — is refused at `CliApplication.Create` with a message
naming the shapes that work. **A shape Portico cannot construct is a startup error, never a
dispatch-time one** (POR-144); the same rule holds for collection options, where `Queue<T>`,
`Stack<T>`, `Collection<T>` and `LinkedList<T>` are refused rather than half-bound.

### Collection options — many values, or a repeated option

The widest type surface in the framework. **Both invocation forms bind**, and they are
interchangeable — most frameworks support one or the other:

```
admin index --files a b c          one option, many values
admin index --file a --file b      the option repeated
```

(Not to be confused with [Both option forms bind](#both-option-forms-bind) below, which is about
`--opt value` versus `--opt=value` — a different axis.)

#### The shapes that bind

Every type here is covered by a test in `test/Portico.Tests/CliCollectionTypes_Should.cs`, and the
table itself is checked against the framework's allow-list by `CliCollectionBindingDocs_Should` — if
a shape is added or removed and this table is not updated, the build fails.

| Declared type | Order | Duplicates |
|---|---|---|
| `T[]` | as typed | kept |
| `List<T>` | as typed | kept |
| `IEnumerable<T>` | as typed | kept |
| `IList<T>` | as typed | kept |
| `ICollection<T>` | as typed | kept |
| `IReadOnlyList<T>` | as typed | kept |
| `IReadOnlyCollection<T>` | as typed | kept |
| `ImmutableArray<T>` | as typed | kept |
| `ImmutableList<T>` | as typed | kept |
| `IImmutableList<T>` | as typed | kept |
| `HashSet<T>` | unspecified | **deduped** |
| `ISet<T>` | unspecified | **deduped** |
| `IReadOnlySet<T>` | unspecified | **deduped** |
| `ImmutableHashSet<T>` | unspecified | **deduped** |
| `IImmutableSet<T>` | unspecified | **deduped** |
| `SortedSet<T>` | **sorted** | **deduped** |
| `ImmutableSortedSet<T>` | **sorted** | **deduped** |

That is the whole reason to pick one over another: a set shape silently drops
`--tags a b a` to two values, and the sorted shapes reorder. If you want what the user typed, in the
order they typed it, use a list shape.

`T` is any type an option can bind — a primitive, an enum, `string`, `TimeSpan`, `Guid`, `Uri`,
`DateTime`, or a type of your own carrying a `[TypeConverter]`.

**A shape that is not on this list is refused at `CliApplication.Create`**, naming the option and the
shapes that work — never at dispatch. `Queue<T>`, `Stack<T>`, `Collection<T>` and `LinkedList<T>` are
the common near-misses.

#### An absent optional collection binds empty, not null

```csharp
int Run([CliOption("--tags")] string[]? tags = null)
```

```
run --tags a b     ->  ["a", "b"]
run                ->  []            not null
```

So a handler can iterate without a null check. The `= null` in the signature is what C# forces —
a parameter default must be a compile-time constant, and `null` is the only one a collection type
can express — not what the framework binds.

Two reasons, and the first is a fact rather than a preference. **A map option in the same position
has always bound an empty dictionary**, so `null` here made two collection-shaped options in one
signature behave differently for no reason a user could see. And **argv has no syntax for an
explicitly empty list**, so "absent" and "supplied with zero values" are indistinguishable at the
terminal; a distinction the CLI surface cannot express should not survive into the handler.

A collection with no `?` and no default is **required**, not optional, and still errors when absent.
A `CliFlag?` is unaffected — absent genuinely means "off", and `null` is how that is spelled.

#### A `string` is a scalar, not a collection

`string` is `IEnumerable<char>` structurally. Portico does not treat it as a collection, so
`[CliOption("--name")] string name` binds one value rather than a list of characters.

#### The comma separates only in the environment

`PORTICO_TAGS=a,b,c` binds three values. `--tags a,b,c` binds **one** value that contains commas.

The asymmetry is deliberate: a single environment variable has no other way to carry a list, while
argv already does — you repeat the option or pass several values. The consequence is that a value
containing a comma cannot come from the environment, and argv is the escape hatch. See
[Environment-variable fallback](#environment-variable-fallback) above for the full table.

What the variable means depends on the option's shape, because a string has to answer questions argv
never asks:

| Shape | Environment form | Notes |
|---|---|---|
| scalar | `PORTICO_API_TOKEN=abc` | converted exactly as a typed value would be |
| `CliFlag?` | `PORTICO_VERBOSE=1` | on unless the value is empty, `0`, `false` or `no` (any case) |
| collection | `PORTICO_TAGS=a,b,c` | comma-separated (see below); a value containing a comma must come from argv |
| **map** | — | **not supported, and it throws at startup** |

**Set-but-empty is off.** `docker run -e FOO` and an undefined variable in a compose file both pass
`FOO=`, so treating "the variable exists" as "the flag is on" would silently enable a flag nobody
asked for — on the most common container idiom there is.

**A collection's comma is an environment-only separator.** One variable has no other way to carry a
list, so `PORTICO_TAGS=a,b,c` binds three elements. argv does not split — you repeat the flag
(`--tag a --tag b`), and `--tag "Smith, John"` is one element. The consequence is that the two
channels disagree for a value containing a comma: `--tag "Smith, John"` is one element, but
`PORTICO_TAGS=Smith, John` is two, and there is no way to escape it. If an element can contain a
comma, take it from the command line, or take the whole thing as a scalar and split it yourself.

**The map case is declined, loudly.** One variable cannot carry key/value pairs without nesting one
separator inside another (`PORTICO_SHARD=eu=3,us=5`), and every such encoding breaks on the first
value that contains either separator. So `EnvironmentVariable` on a map option throws
`CliConfigurationException` from `CliApplication.Create` — before a single command runs — rather than
binding nothing at dispatch. Take the value as a scalar and parse it in your handler, where you choose
the format.

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
--timeout 90s             --timeout 1h30m       --timeout 500ms
--timeout "30 seconds"    --timeout "5 min"     --timeout "1.5 hours"
--timeout "2 days 4 hrs"  --timeout PT30S       --timeout 00:00:30
```

`TimeSpan?` behaves identically — which it did not, until a worked example caught it (the bug is in
the changelog).

**The grammar**, so the boundary is discoverable rather than guessable:

| Form | Shape | Examples |
|------|-------|----------|
| Duration | one or more `<number><unit>` pairs, whitespace optional | `90s`, `1h30m`, `500 ms`, `2 days 4 hrs` |
| .NET | `TimeSpan.Parse` | `00:00:30`, `1.12:00:00` |
| ISO 8601 | `XmlConvert.ToTimeSpan` | `PT30S`, `PT1H30M` |

Units are `ms`, `s`, `m`, `h`, `d`, and their spelled-out forms (`msec`, `sec`, `min`, `hr`, `day`,
each also plural and each also fully written out). Matching is case-insensitive, and the number may
be fractional — `0.5d` is twelve hours.

> **A bare number is refused.** `--timeout 30` does not bind thirty seconds; to .NET's `TimeSpan`
> parser a bare number is a *day* count, so it would silently mean thirty **days**. Portico rejects
> it and names the repairs (`30s`, `30 seconds`, `00:00:30`) rather than guessing. Reinterpreting it
> as seconds would be friendlier and is deliberately not done — the same string would then mean one
> thing in Portico and another in every other .NET tool. This is a .NET-wide trap that Portico
> declines to inherit quietly, not a defect in any particular framework.

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

### Both option forms bind

```
admin db seed --rows 250        admin db seed --rows=250
admin reindex '--shard[eu]' 3   admin reindex '--shard[eu]=3'
admin drain --timeout "90 sec"  admin drain --timeout="90 sec"
```

The space form and the glued GNU form (`--opt=value`) are equivalent, for scalars, collections and
maps alike. Everything after the **first** separator is the value, verbatim — `--filter=name=foo`
binds `name=foo`, and a quoted value with spaces survives.

After the POSIX `--` terminator, a token that looks like an option is a positional and is left exactly
as typed: `echo -- --name=x` passes `--name=x` through as text.

Short options glue POSIX-style (`-n5` ≡ `-n 5`), and `-n=5` is read as an assignment (`5`), not as the
literal value `=5`.

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

Emits a self-contained bash, zsh or PowerShell completion script for the application's routes. Wire it to a
hidden command and `admin completion bash > /etc/bash_completion.d/admin`.

### Middleware — the `IActionFilter` analogue

`CliMiddleware` gets `OnExecutingAction` / `OnActionExecuted` / `OnError`, and can declare its own
options (`--verbose`, `--timing`) which become available to every command. `OnActionExecuted` runs
from a `finally`, so it is the symmetric partner of `OnExecutingAction` even when the command threw.

Middleware is constructed by you and cloned per dispatch, so it can take constructor dependencies —
`Portico.DependencyInjection` resolves them: `cfg.UseMiddleware<AuditMiddleware>(serviceProvider)`.

Two ship in the box: `CliTimingMiddleware` (`--timing`) and `CliTracingMiddleware`
(`--trace-level`).

## Testing

### Contract validation — `CliContractValidator<T>`

Runs every `[CliCommandExample]` against a `DispatchProxy`-backed application. An example that
fails to dispatch, or dispatches to the wrong handler, is a test failure.
`Enumerate()` returns one `CliContractExample` per example — handler, arguments, and values —
so a single `[Theory]` gives you one red/green per example.

### End-to-end testing — `CliTestHarness`

```csharp
var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands(new MyService()));
harness.Run("myapp seed --rows 10").ExpectExit(0).ExpectOut("Seeded 10 rows");
harness.Run("myapp seed --rows abc").ExpectExit(2).ExpectError("invalid");
harness.Run("myapp confirm-delete", input: "y\n").ExpectExit(0);
```

Each `Run` builds a fresh `CliApplication` with a dedicated in-memory `ICliConsole`.
Exit code, stdout, stderr and stdin injection — no `Console.SetOut`, no process spawn,
no parallel-test interference. Four chainable assertions: `ExpectExit`, `ExpectOut`,
`ExpectError`, `ExpectNotError`.

Both types ship inside the core `Portico` package. Nothing extra to install.

## See also

- [Composing CLIs](../how-to/compose-clis.md) — mounting several contracts into one binary
- [Analyzer rules](analyzer-rules.md) — the ten compile-time checks
- [Extensibility](../explanation/extensibility.md) — what you can extend, and what is sealed
