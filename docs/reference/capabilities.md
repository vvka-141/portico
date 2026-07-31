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
`PORTICO_API_TOKEN` once in the container and stops typing it. **A variable set to nothing does not
count as the environment having said anything** — it falls through to the default, on every option
shape; the per-shape table below spells that out.

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

**On a collection, it comma-splits** — the same rule as the
[environment-variable path](#environment-variable-fallback), for the same reason: one authored
string has to carry several values, and argv remains the escape hatch for a value that contains a
comma.

```csharp
[CliOption("--regions", DefaultValue = "eu,us")] string[]? regions = null   // ["eu", "us"]
```

A bad element is a `CliConfigurationException` at `CliApplication.Create`, naming the element rather
than the whole list.

**On a map it is refused**, at `Create`. One string cannot carry key/value pairs without an encoding
that nests one separator inside another and breaks on the first value containing either — the same
reasoning that declined `EnvironmentVariable` on map options. It used to be accepted and then
silently ignored.

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
admin reindex --shard eu=3 us=5
```

First-class, not a parsing trick: the key is a string, the value is converted like any other option.

**Two spellings bind identically.** `key=value` is the canonical one because it needs no shell
quoting; the bracket form is the query-string shape the [HTTP metaphor](../explanation/charter.md)
derives, and it is the only one that can carry a key containing `=`.

| You type | Key | Value |
|---|---|---|
| `--shard eu=3` | `eu` | `3` |
| `--shard=eu=3` | `eu` | `3` |
| `'--shard[eu]' 3` | `eu` | `3` |
| `'--shard[eu]=3'` | `eu` | `3` |
| `--shard dsn=host=db;port=5432` | `dsn` | `host=db;port=5432` — the **first** `=` splits |
| `'--shard[a=b]' 3` | `a=b` | `3` — a key containing `=` needs the brackets |

Mix them freely in one invocation; they fill the same dictionary, and a key supplied twice is the same
duplicate-key usage error either way.

> **The bracket form needs shell quoting.** The brackets in `--shard[eu]` are
> filename-expansion characters. On **zsh** (the default shell on macOS) an
> unquoted `--shard[eu]` fails with `zsh: no matches found` before Portico ever
> sees it — the shell aborts the command, so no diagnostic Portico could emit
> would reach you. Either use `--shard eu=3`, or quote the option name:
> `'--shard[eu]'` / `--shard\[eu\]`. Inside `[CliCommandExample]` attributes the
> value is an argv array and must *not* be quoted.

The split of `key=value` happens in the map binder, not in the parser — the parser does not know an
option's declared type, so `--shard eu=3` reaches it as an ordinary option with one bare value. This is
why the scalar rule below (*everything after the first separator is the value, verbatim*) is unchanged:
for a map, that value is then read as a pair.

**A repeated key accumulates when the value type is a collection.** Headers, labels and selectors
repeat keys as a matter of course, so the declared value type chooses the semantics:

```csharp
[CliOption("--header")] Dictionary<string, string>?   one    // repeated key → usage error
[CliOption("--header")] Dictionary<string, string[]>? many   // repeated key → accumulates
```

```
admin call '--header[Accept]' json html          one option, several values
admin call --header Accept=json Accept=html      the option's key repeated
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

**The nullable form of a shape is the same shape.** `ImmutableArray<T>` is a struct, so
`ImmutableArray<T>?` is the only way to write an *optional* immutable-array option that reads as
optional at the declaration site — and it binds exactly as `ImmutableArray<T>` does, including the
empty-when-absent rule below. There is no row for it in the table because it is not a separate shape.

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

**Set-but-empty means absent — for every shape.** `docker run -e FOO` and an undefined variable in a
compose file both pass `FOO=`, so a variable set to nothing is treated exactly as an unset one: the
option falls back to its declared default. A flag stays off, a collection stays at its default, and
`PORTICO_PORT=` on an `int` option leaves `8080` in place instead of failing the process with
*"'' is not a valid value for Int32"*.

> This used to be a flag-only rule, and the other shapes each answered it differently: the collection
> path agreed by accident, and the scalar path bound the empty string — so a containerised tool
> refused to start when its orchestrator passed an empty variable, at the worst possible moment and
> for a reason its operator never chose. One rule now, in one place (POR-161).

The cost is that the environment cannot say *"explicitly the empty string"*, nor a whitespace-only
one. Nothing becomes unexpressible: argv still says it, as `--name ""`. A variable is a source of
**defaults**, and an empty answer from a default source is not an answer.

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
admin reindex --shard eu=3      admin reindex --shard=eu=3
admin drain --timeout "90 sec"  admin drain --timeout="90 sec"
```

The space form and the glued GNU form (`--opt=value`) are equivalent, for scalars, collections and
maps alike. Everything after the **first** separator is the value, verbatim — `--filter=name=foo`
binds `name=foo`, and a quoted value with spaces survives. On a **map** option that value is then read
as `key=value` (see [Map options](#map-options--the-cfgenvprod-analogue)), which is the one place a
second separator means something.

After the POSIX `--` terminator, a token that looks like an option is a positional and is left exactly
as typed: `echo -- --name=x` passes `--name=x` through as text.

### A positional after an option needs the `--` terminator

**A bare token following an option belongs to that option.** So a positional written *after* an option
is consumed by it, and the command fails:

```
admin compile --output out.dll main.cs      ✗ --output takes both values
admin compile main.cs --output out.dll      ✓ natural order, no ceremony
admin compile --output out.dll -- main.cs   ✓ the terminator separates them
```

The failure is loud and names the fix — it does not silently bind the wrong thing:

```
Command 'compile {source}' expects 1 argument, got 0.
Option '--output' consumed 2 values — a bare token following an option belongs to that option.
If 'main.cs' is a positional argument, pass it after the '--' terminator (e.g. '… -- main.cs').
```

Most CLIs resolve this implicitly. Portico does not, and the reason is that a **variadic** option
followed by a positional has no correct greedy answer — `--tags a b main.cs` is indistinguishable from
three tags. Deciding it would mean tokenizing against the matched route's positional arity, inverting
the parser's dependency on route matching. The full reasoning and the bar to reopen it are in
[ROADMAP.md](../ROADMAP.md#implicit-positional-after-option-no-the-terminator-stays-explicit-resolved-2026-07-30-por-82).

### Short options bundle, POSIX-style

Take a command declaring four shorts — two flags, a scalar and a map:

```csharp
[CliRoute("sync")]
[CliCommandExample("sync -av")]
public int Sync(
    [CliOption("--all|-a")]     CliFlag? all = null,
    [CliOption("--verbose|-v")] CliFlag? verbose = null,
    [CliOption("--number|-n")]  int number = 0,
    [CliOption("--env|-e")]     Dictionary<string, string>? env = null)
```

| You type | Portico reads |
|---|---|
| `-av` | `-a -v` — a cluster of flags |
| `-avn5` | `-a -v -n 5` — a scalar in the cluster takes the rest as its value |
| `-n5` | `-n 5` — the glued POSIX form |
| `-n=5` | `-n=5` — an assignment, so the value is `5`, never `=5` |
| `-e[region] eu` | unchanged — a map short keeps its `[key]` |
| `-e region=eu` | unchanged — the shell-safe map form is already two tokens |
| `-ax` | unchanged — `x` is not a declared short |
| `--all` | unchanged — a long option is never split |

Every row is executed by `CliShortOptionDocs_Should`, which also fails if this table lists a form the
tests do not cover.

The rule behind the right-hand column is **never introduce ambiguity**. Splitting happens only when
every letter in the cluster is a short this application declared, so an unknown letter leaves the
token whole and you get *"Unrecognized option(s): -ax"* rather than a silent misreading. Long options,
assignments and map shorts are left alone for the same reason: each of them means something the split
would destroy.

#### One letter, one arity — application-wide

Bundling has a cost worth knowing before it surprises you. `-fx` has to be split **before** the parser
knows which command it belongs to, so a letter's arity is agreed across the whole application, not per
command.

If two commands declare the same letter differently — `-f` as a `CliFlag?` on one route and as a
`string` on another — Portico cannot know which split `-fx` means, so **the letter stops bundling
everywhere**, including on the command that declares it consistently. You will see:

```
Unrecognized option(s): -fx. Did you mean: -f, -x?
```

Nothing else changes: `-f -x` written out still binds on every route, and both commands keep working.
Only the glued form goes away. `CliApplication.Create` traces a warning naming both routes and the
letter, so the cause is visible at startup rather than at the first user report.

The repair is to give one of the two options a different letter.

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

### A placeholder's name is not part of a route's identity

`[CliRoute("x {first}")]` and `[CliRoute("x {second}")]` are the *same route* as far as a command line
is concerned — **the name is invisible when you type**, so `x foo` cannot indicate which you meant. If
nothing else separates them, both commands are unreachable, and Portico refuses to build:

```
Routes 'x {first}' and 'x {second}' differ only in the name of a placeholder, and declare the same
options — so no command line can tell them apart and neither would ever run. Give them different
literal segments, or different options, or merge them into one command.
```

Reported at `CliApplication.Create`, like every other configuration error, instead of on each of the
user's invocations.

**"If nothing else separates them" is exact.** Same-shape routes whose *options* differ are a supported
shape and still build, because [route ranking](#route-ranking-is-a-tie-breaker-not-overload-selection)
can resolve them:

```csharp
[CliRoute("y {first}")]  int First(string first,  [CliOption("--alpha")] string alpha = "");
[CliRoute("y {second}")] int Second(string second, [CliOption("--beta")]  string beta  = "");
```

`y foo --alpha 1` and `y foo --beta 2` each dispatch. `y foo` alone is still ambiguous at run time —
correctly, because those routes *are* reachable, just not by that command line.

The placeholder's name still matters everywhere else: it names the argument in `--help`, in error
messages, and in shell completion. It is collapsed for identity only.

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

Each `Run` builds a fresh `CliApplication` with a dedicated in-memory `ICliConsole` and does not
spawn a process. To capture handlers that use `Console.WriteLine` or `Console.ReadLine`, the harness
temporarily redirects the process-global `Console.Out`, `Console.Error` and `Console.In` streams.
A semaphore serializes harness runs with one another, but it cannot serialize them against unrelated
parallel tests that touch `Console`; put those tests in a non-parallel collection.

Four chainable assertions ship: `ExpectExit`, `ExpectOut`, `ExpectError`, `ExpectNoError`.

Both types ship inside the core `Portico` package. Nothing extra to install.

## See also

- [Composing CLIs](../how-to/compose-clis.md) — mounting several contracts into one binary
- [Analyzer rules](analyzer-rules.md) — every live compile-time check
- [Extensibility](../explanation/extensibility.md) — what you can extend, and what is sealed
