# Your first operational command

[The first CLI](../tutorial/first-cli.md) teaches the loop: one route, one example, a green contract
test, break it and watch the build go red. Do that one first — this page assumes it.

This one is about the other half. Portico's tagline says *the command surface for .NET backend
services*, and this is what that means in practice: a command an on-call engineer runs against a
container at two in the morning. Every step below adds exactly one capability that the job needs and
that a general-purpose argument parser does not give you.

The command we are building, in full:

```
admin db backfill --ids 41 42 43 --dry-run --timeout "5 min"
```

**Everything on this page is quoted from `examples/AdminCli`, which CI builds and contract-tests.**
The transcripts are copied from running the binary it produces. Nothing here is written by hand,
because a walkthrough that drifts is worse than no walkthrough — that is the whole thesis of the
framework, and it applies to its own documentation first.

This page does not argue why the contract-test model is worth having ([the README](../../README.md)
does that) and does not compare Portico to anything else
([alternatives.md](../explanation/alternatives.md) does that).

---

## The whole contract

```csharp
[Description("Backfill a column for specific rows")]
[CliRoute("db backfill")]
[CliCommandExample("db backfill --ids 41 42 43 --dry-run")]
[CliCommandExample("db backfill --ids 41 42 43 --timeout \"5 min\"")]
[CliCommandExample("db backfill")]
Task<int> BackfillAsync(
    [CliOption("--connection-string|-c", "Postgres connection string",
        EnvironmentVariable = "PGCONNSTR", Sensitive = true)]
    string? connectionString = null,
    [CliOption("--ids", "Row ids to backfill (repeatable)")] int[]? ids = null,
    [CliOption("--dry-run", "Print the plan; change nothing")] CliFlag? dryRun = null,
    [CliOption("--timeout", "Give up after this long")] TimeSpan? timeout = null,
    CancellationToken cancellation = default);
```

Six capabilities, one signature, no builder. The rest of this page is what each one buys.

---

## 1. The credential that never appears anywhere

```csharp
[CliOption("--connection-string|-c", "Postgres connection string",
    EnvironmentVariable = "PGCONNSTR", Sensitive = true)]
string? connectionString = null
```

`EnvironmentVariable` means an operator sets it once in the container and stops typing it. argv still
wins when both are present, so a one-off override needs no config change.

`Sensitive = true` means the value never reaches anything the framework writes — trace output, timing
output, conversion errors, the echo of a mistyped command. In a container **stderr is the log
stream**, so a parse error that helpfully quotes your connection string has published it.

The two together create the question that stopped everyone else shipping this: if help mentions the
variable, does help leak the secret? Portico's answer is to print the **name** and never read the
**value**:

```
$ PGCONNSTR="Host=secret-host;Password=hunter2" admin db backfill --help

Options:
  --connection-string, -c  Postgres connection string  (env: PGCONNSTR)
  --ids                    Row ids to backfill (repeatable)
  --dry-run                Print the plan; change nothing
  --timeout                Give up after this long
```

The operator learns which variable to set. The password is not on the screen, and was never read.

> Why `string?` and not `string`: the option must be *optional* for the environment to have anything
> to fill. Whether missing configuration is fatal is the handler's call, not the parser's — see
> step 5.

## 2. Ids: both forms, and what "absent" means

```csharp
[CliOption("--ids", "Row ids to backfill (repeatable)")] int[]? ids = null
```

Both spellings bind, and they are the same thing:

```
admin db backfill --ids 41 42 43
admin db backfill --ids 41 --ids 42 --ids 43
```

Seventeen collection shapes work here — `List<T>`, the read-only interfaces, the set shapes, the
immutable ones. [The full table](../reference/capabilities.md#collection-options--many-values-or-a-repeated-option)
says which deduplicate and which sort, because that is the reason to pick one.

**Absent, `--ids` binds an empty array — not `null`:**

```
$ PGCONNSTR="Host=db" admin db backfill
no ids given; nothing to backfill.
```

Be precise about what that guarantee is. There is no `NullReferenceException` waiting in the handler,
which is the failure it exists to prevent. But the `?` that makes the option optional is also what
makes the C# compiler ask, so under nullable reference types you still acknowledge it:

```csharp
var rows = ids ?? [];
```

The framework's promise is about the value, not about the annotation.

## 3. `--dry-run`: presence, not a value

```csharp
[CliOption("--dry-run", "Print the plan; change nothing")] CliFlag? dryRun = null
```

`CliFlag?` is presence-only. `--dry-run` turns it on, absent leaves it off, and there is no
`--dry-run true` to get wrong. A `bool` would be two-state and would need a value — which is
occasionally what you want, and is a
[different declaration](../reference/capabilities.md#cliflag-versus-bool--presence-versus-value).

For an operational command this distinction earns its keep: `--dry-run false` is exactly the sort of
thing someone types at 2am expecting it to mean "no dry run".

```
$ PGCONNSTR="Host=db;Username=svc" admin db backfill --ids 41 42 43 --dry-run
dry run: would backfill 3 row(s) within 60s.
```

## 4. `--timeout`: the way an operator writes a duration

```csharp
[CliOption("--timeout", "Give up after this long")] TimeSpan? timeout = null
```

All of these bind: `5 min`, `90s`, `1h30m`, `500ms`, `2 days 4 hrs`, `PT5M`, `00:05:00`.

```
$ PGCONNSTR="Host=db" admin db backfill --ids 41 --timeout 90s --dry-run
dry run: would backfill 1 row(s) within 90s.
```

**A bare number is refused**, and this is the one worth knowing about:

```
$ PGCONNSTR="Host=db" admin db backfill --ids 41 --timeout 5
Value '5' for option '--timeout' is invalid. Ambiguous duration '5' — a bare number means
DAYS to .NET, so '5' would be 5 days. Say which unit you mean: '5s', '5 seconds', '5m',
'5 days' — or use the .NET form '00:00:5'.
```

To .NET's `TimeSpan` parser a bare number is a *day* count. On a backfill, `--timeout 5` silently
meaning five days is an incident. Portico refuses rather than guessing, and does not reinterpret it
as seconds either — the same string would then mean one thing here and another in every other .NET
tool.

## 5. Exit codes an orchestrator can read

A handler returns an `int`, or throws `CliExitException` with the code it wants:

```csharp
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new CliExitException(
        "No connection string. Pass --connection-string, or set PGCONNSTR.")
    {
        ExitCode = CliExitException.UsageErrorExitCode,
    };
}
```

```
$ admin db backfill --ids 41 42
No connection string. Pass --connection-string, or set PGCONNSTR.
$ echo $?
2
```

The named constant matters more than the number. A pipeline reading `$?` can tell **2 — you
configured this wrong** from **1 — the run failed**, and retrying is the right response to exactly one
of those. `CliExitException` carries `SuccessExitCode`, `RuntimeErrorExitCode`, `UsageErrorExitCode`,
`CancelledExitCode` and `TerminatedExitCode` so the distinction survives contact with a hurried
handler.

> **A `catch (Exception)` around your own work will swallow this** and turn a failed command into
> exit 0 — a green build over a broken backfill. Portico ships
> [POR013](../reference/analyzer-rules.md#por013) to catch it at build time, because nothing can
> catch it at run time.

## 6. Shutting down when the orchestrator says so

```csharp
CancellationToken cancellation = default
```

Declare the parameter and the framework wires it. No registration, no `Console.CancelKeyPress`.

```csharp
using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
deadline.CancelAfter(budget);

foreach (var id in rows)
{
    deadline.Token.ThrowIfCancellationRequested();
    ...
}
```

Linking the token to the `--timeout` budget is the whole pattern: one token that is cancelled either
because the operator asked or because the deadline passed, and a loop that checks it.

Portico maps the signal to a POSIX exit code — SIGINT to **130**, SIGTERM to **143** — unconditionally,
on the synchronous path as well as the asynchronous one, and it keeps the two distinct. That last
part is what lets a container orchestrator tell `docker stop` from someone pressing Ctrl+C.

Under `docker stop`, which sends SIGTERM and then waits, that means the backfill finishes the row it
is on, stops, and the shell reports 143 rather than a killed process.

> **The mapping is asserted in CI**, not just described here:
> `CliApplicationAutoCancel_Should` pins `CancelledExitCode == 130` and `TerminatedExitCode == 143`
> and exercises the remap. The container run above is an illustration of behaviour the test suite
> proves — this page does not print an exit code the repository cannot reproduce.

---

## Where the proof lives

Nothing above is a claim you have to take on trust:

| Claim | Proved by |
|---|---|
| Every example dispatches | `AdminContract_Should.Dispatch`, one case per `[CliCommandExample]` |
| `--ids 41 42 43` binds `[41, 42, 43]` | `Bind_A_Repeated_Collection_Option` |
| Absent `--ids` binds empty, not null | `Bind_An_Empty_Collection_When_The_Option_Is_Absent` |
| `--timeout "5 min"` binds five minutes | `Bind_A_Compact_Duration_On_The_Backfill` |
| `--timeout 5` is refused | `Refuse_A_Bare_Number_As_A_Duration` |
| Help names `PGCONNSTR` and never its value | `Name_The_Environment_Variable_In_Help_Without_Reading_It` |
| The environment fills the connection string, and argv beats it | `Read_The_Connection_String_From_The_Environment`, `Let_Argv_Beat_The_Environment` |
| A missing connection string exits 2 | `Refuse_A_Backfill_With_No_Connection_String_As_A_Usage_Error` |
| SIGINT → 130, SIGTERM → 143 | `CliApplicationAutoCancel_Should` |

Change the contract without changing the walkthrough and one of those goes red.

## Next

- [Capabilities](../reference/capabilities.md) — the whole option surface, every entry backed by a test
- [Compose CLIs](compose-clis.md) — mounting this tool inside a larger one
- [The alternatives, honestly](../explanation/alternatives.md) — when a different framework is the right answer
