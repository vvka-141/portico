# Portico — Extensibility Guide

> **Purpose.** Document the escape hatches that exist today. If you're tempted to propose
> a new hook, a new renderer override, or to unseal `CliApplication`, check here first —
> the capability you want is probably already covered.
>
> **Rule of thumb (from CHARTER §4.7 — "Richness without stiffness").** Every extension
> is *opt-in* and *additive*. You bring your own subclass / bundle / attribute; the
> framework composes it. Nothing here is mandatory.

---

## Switches vs. boolean scalars

Portico deliberately separates two concepts that other frameworks conflate:

| Shape | Declare as | Command line | Semantics |
|---|---|---|---|
| **Switch** (presence-only) | `CliFlag? verbose` | `--verbose` (no value) | present → `CliFlag.Default`, absent → `null` |
| **Boolean scalar** (two explicit states) | `bool? logging` | `--logging true` / `--logging false` | parsed via `TypeConverter` — `--logging` alone is a usage error |

Rationale: `bool` suggests "two values you can observe," but a switch only ever has *one* value
(it was given). Routing `bool` through the flag materializer — as older .NET CLI frameworks do —
means `--verbose true` is silently rejected and users get confused. Portico' rule: **if you
want a switch, type it `CliFlag?`. If you want a boolean value, type it `bool`/`bool?`.** No
overlap, no surprises.

---

## What you can extend — at a glance

| You want to… | Do this |
|---|---|
| Customize option parsing / type conversion | Subclass `CliOptionAttribute`; override `CanAccept` |
| Customize argument conversion | Subclass `CliArgumentAttribute`; override `CanAccept` |
| Group related options into a reusable struct | Subclass `CliOptions` |
| Add cross-cutting behavior around every command | Subclass `CliMiddleware`; override `OnExecutingAction` / `OnActionExecuted` / `OnError` |
| Resolve a command handler from your own DI container | `cfg.AddCommands<IMyCommands>(() => sp.GetRequiredService<IMyCommands>())` |
| Validate a single property with standard rules | DataAnnotations: `[Range]`, `[RegularExpression]`, `[StringLength]`, … |
| Validate combinations of properties on a bundle | Implement `IValidatableObject` on the bundle |
| Return a specific exit code from a handler | Return `int`, or `throw new CliExitException(msg) { ExitCode = 42 }` |
| Customize `--version` output | `cfg.WithVersion(() => $"mycli {SemVer}\ncommit {Git.Sha}")` |
| Add a `version` subcommand alongside `--version` | `cfg.WithVersion(v => v.Text("…").Triggers("--version", "-V", "version"))` |
| Replace help triggers (e.g. add `/?` on Windows) | `cfg.WithHelp(h => h.Triggers("--help", "-h", "-?", "/?"))` |
| Disable help entirely (daemon / headless CLI) | `cfg.SuppressHelp()` |
| Get the ambient `CancellationToken` | Declare a `CancellationToken` parameter — framework injects it |
| Emit shell completion (verb-level) | `app.EmitCompletion(shell, exeName, output)` — **you wire it**: expose a `completion` subcommand that calls this. Scope is verb-level only (proposes the next route segment); options/values do not complete (`app deploy --<TAB>` yields nothing). Parked in the [ROADMAP](../ROADMAP.md) until demand appears |
| Fall back to an env var when an option is absent | `[CliOption("--port", EnvironmentVariable = "PORT")] int port` |
| Share a route prefix across every method on a type | `[CliRoute("db")]` on the interface or class — prepended to each method's route; class wins over inherited interface |
| Bind a `CancellationToken` to a user-supplied timeout | `[CliOption("--timeout")] CancellationToken timeout` — accepts `30s`/`5m`/`PT2M`/`00:00:30` |
| Write hermetic CLI integration tests | `CliTestHarness.ForApplication(cfg => …).Run("app cmd").ExpectExit(0)` |
| Test a handler that calls `CliPrompt` / `Console.ReadLine` | `harness.Run("app delete foo", input: "y\n")` — feeds stdin |
| Accept a multi-value option as a list | `[CliOption("--envs")] List<string> envs` — also `T[]`, `IEnumerable<T>`, `IList<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `ImmutableArray<T>`, `ImmutableList<T>`, `IImmutableList<T>` |
| Accept a multi-value option as a set (dedup) | `[CliOption("--tags")] HashSet<string> tags` — also `SortedSet<T>`, `ISet<T>`, `IReadOnlySet<T>`, `ImmutableHashSet<T>`, `IImmutableSet<T>`, `ImmutableSortedSet<T>` |
| Declare a positional argument | a `{name}` placeholder in the route: `[CliRoute("deploy {env}")] int Deploy(string env)`. The route string is the command's path in full — an argument has no other way to get a position |
| Add a description (and a display name) to an argument | `int Deploy([CliArgument("Target environment name", Name = "ENV")] string env)` — describes the `{env}` the route already declares; it never adds a segment |
| Time every command invocation (opt-in via `--timing`) | `cfg.UseMiddleware(new CliTimingMiddleware())` — prints `[timing] <invocation> ... N ms` to stderr |
| Route `Trace.*` output to the console (opt-in via `--trace-level`) | `cfg.UseMiddleware(new CliTracingMiddleware())` — writes through the app's `ICliConsole`. **Caveat:** `Trace.Listeners` is process-global, so concurrent in-process invocations cross-talk; treat as single-writer per process |
| Get graceful Ctrl+C → exit 130 with zero boilerplate | `.RunAsync()` / `.RunAsync(args)` auto-wires `Console.CancelKeyPress` |
| Ask for a line with an optional default | `CliPrompt.GetLine("Environment", defaultValue: "production")` |
| Read a password without echo | `CliPrompt.GetPassword("Password")` |
| Guard a destructive operation by typing a word | `CliPrompt.ConfirmByTyping("Drop db?", expected: "DELETE")` |

If your need isn't in the table, it probably means one of:
- You haven't discovered the right primitive yet — re-read the sections below.
- The need is speculative — what exact user scenario are you blocked on today?
- It's a genuine gap — open an issue with the concrete scenario.

---

## Extension point details

### 1. Custom option types via `CliOptionAttribute` subclass

`CliOptionAttribute.CanAccept(Type, out TypeConverter)` is virtual. Override it to add
support for types the framework doesn't know about.

```csharp
public sealed class CliJsonOptionAttribute : CliOptionAttribute
{
    public CliJsonOptionAttribute(string spec) : base(spec) { }

    public override bool CanAccept(Type optionType, out TypeConverter converter)
    {
        converter = new JsonTypeConverter(optionType);
        return true;   // I accept anything — I'll deserialize JSON into it.
    }
}
```

Use it on a parameter the same way you'd use `[CliOption]`:

```csharp
public int Import([CliJsonOption("--manifest|-m")] Manifest manifest) { ... }
```

#### Analyzer coverage gap for derived attributes

The Roslyn analyzers POR003, POR009 and POR010 match `[CliOption]` and `[CliArgument]` by name.
A derived attribute (`[CliJsonOption]`) is invisible to them — they will not flag a malformed
spec, a duplicate alias, or an unconvertible type on it.

**Runtime still validates.** `CliOptionMaterializer` checks the same invariants at dispatch time
and throws `CliConfigurationException` from `CliApplication.Create` before a single command runs.
The gap is compile-time feedback only: a broken derived attribute fails at startup, not at build.

If build-time coverage matters to you, keep `[CliOption]` on the parameter and move the custom
conversion into a `TypeConverter` registered via `TypeDescriptor` — the analyzers will see it.

### 2. Custom argument types via `CliArgumentAttribute` subclass

Same mechanism for positional arguments. Override `CanAccept` to plug in a custom
`TypeConverter`.

### 3. Option grouping — `CliOptions`

A bundle is a plain class with `[CliOption]` properties. Declare a bundle parameter on
any action method; the framework materializes it. Use this when a command has 3+
options that naturally cluster.

```csharp
public sealed class PagingOptions : CliOptions
{
    [CliOption("--page|-p", DefaultValue = "1")] public int Page { get; set; }
    [CliOption("--size|-s", DefaultValue = "50")] public int Size { get; set; }
}

[CliRoute("list")]
public int List(PagingOptions paging) { ... }
```

Bundles are regular classes — you can unit-test them, re-use them across commands,
apply DataAnnotations to their properties, and implement `IValidatableObject` for
cross-property rules.

### 4. Cross-cutting behavior — `CliMiddleware`

A `CliMiddleware` applies to *every* registered command. Override lifecycle methods to
run code before / after / on-error around any invocation. Register one call per middleware
via `UseMiddleware`; chain calls to add multiple (they run in declared order).

```csharp
public sealed class TelemetryMiddleware : CliMiddleware
{
    [CliOption("--trace-id")] public string? TraceId { get; set; }

    public override void OnExecutingAction(CliInvocation invocation) { /* start span */ }
    public override void OnActionExecuted(CliInvocation invocation) { /* flush */ }
    public override void OnError(CliInvocation invocation, Exception e) { /* report */ }
}

CliApplication.Create(app => app
    .UseMiddleware(new TelemetryMiddleware())
    .AddCommands(new MyService()));
```

The library ships `CliTracingMiddleware` as a ready-made example. Build your own for
logging, config-file resolution, output-format selection, etc.

### 5. Dependency injection — explicit factories

The framework never depends on `Microsoft.Extensions.DependencyInjection`. Every
registered service carries its own instantiation strategy — either a concrete instance
or a factory the framework invokes on every command.

```csharp
var sp = services.BuildServiceProvider();

CliApplication.Create(cfg => cfg
    .AddCommands<IMyService>(() => sp.GetRequiredService<IMyService>())
    .AddCommands<IOtherService>(() => sp.GetRequiredService<IOtherService>()));
```

Any container works: MEDI, Autofac, Unity, plain factory lambdas. Lifetime is the
factory's concern — return a fresh scope per invocation, a singleton, or anything in
between. The framework does not decide for you.

**The factory is lazy and per-dispatch.** Nothing is constructed at `Create` time, and only the
*matched* route's factory runs — exactly once. `--help` and an unknown command construct nothing at
all, because route metadata comes from reflection over the *Type*, not an instance. So a CLI can
register a `migrate` command that opens a database pool alongside a `health` command that does not,
and `myapp health` will never touch the database.

**Middleware can take constructor dependencies too.** You construct it and hand over the instance,
so the ordinary DI shape works:

```csharp
CliApplication.Create(cfg => cfg
    .AddCommands<IMyService>(() => sp.GetRequiredService<IMyService>())
    .UseMiddleware(sp.GetRequiredService<AuditMiddleware>()));
```

The framework never calls `Activator.CreateInstance` on a middleware — it `MemberwiseClone`s your
instance per dispatch, which carries injected fields through. (Note the shallow copy: a
reference-typed field is *shared* across clones. That is right for an injected stateless service and
wrong for mutable per-invocation state.)

A `CliOptions` **bundle** is different: it *is* `Activator.CreateInstance`d per invocation and so
does need a public parameterless constructor — analyzer rule **POR006** enforces exactly that, and
exempts middleware.

The first-party adapters (`Portico.DependencyInjection`, `Portico.Hosting`) are thin wrappers over
this same factory seam. They are separate packages so the core stays at zero dependencies.

#### Disposables

`CliApplication` is reusable: a single processor can dispatch many commands across the
life of the host. The framework does **not** dispose service instances returned from your
factory — doing so would break the common singleton-factory case (where every `Process()`
call returns the same underlying service).

If your service is `IDisposable`/`IAsyncDisposable` and you want per-invocation lifetime,
manage it inside your handler:

```csharp
[CliRoute("query {sql}")]
public async Task<int> Query(string sql, CancellationToken ct)
{
    // Resolve your own scope; the framework neither creates nor disposes it.
    await using var scope = _sp.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
    // ...
    return 0;
}
```

This keeps the lifetime contract explicit and local.

### 6. Validation — DataAnnotations + `IValidatableObject`

Per-field rules:

```csharp
public int Scale([Range(1, 100)] int n) { ... }
```

Cross-field rules on a bundle:

```csharp
public sealed class RangeOptions : CliOptions, IValidatableObject
{
    [CliOption("--min")] public int Min { get; set; }
    [CliOption("--max")] public int Max { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext ctx)
    {
        if (Min > Max) yield return new ValidationResult("--min must not exceed --max.");
    }
}
```

Violations surface through the same usage-error path as any other option failure.

### 7. Exit codes — `CliExitException`

Return from your handler with a specific exit code:

```csharp
throw new CliExitException("Database unreachable.") { ExitCode = 3 };
```

POSIX conventions are already built in:
- `0` — success
- `1` — runtime error (default for unhandled exceptions)
- `2` — usage error (unrecognized options, invalid values, etc.)
- `130` — cancelled (Ctrl+C, SIGINT)

### 8. Versioning, help, cancellation

- **`WithVersion(string)` / `WithVersion(Func<string>)`** — supplies the string printed
  on `--version`/`-V`. Don't call it → no `--version` flow.
- **`--help` / `-h` / `help` / `?`** — detected automatically. Don't like it? Register
  your own `[CliRoute("help")]` — yours wins.
- **`CancellationToken` parameter** — declare one on any action; the framework injects
  the ambient token. Ctrl+C cancels it and the processor returns exit 130.

### 9. Testability — `ICliConsole`

The framework writes framework-owned output (help, errors, `--version`) through
`ICliConsole`. Inject your own for hermetic tests:

```csharp
sealed class CapturedConsole : ICliConsole
{
    public StringWriter OutWriter { get; } = new();
    public StringWriter ErrWriter { get; } = new();
    public TextWriter Out => OutWriter;
    public TextWriter Error => ErrWriter;
    public TextReader In => TextReader.Null;
}

var console = new CapturedConsole();
var app = CliApplication.Create(cfg => cfg.WithConsole(console)...);
app.Run("app cmd --foo");
Assert.Contains("expected", console.OutWriter.ToString());
```

**Handler code** uses `System.Console` directly — we don't intercept it. If you want
hermetic handler tests, redirect `Console.SetOut`/`SetError` yourself.

---

## What is deliberately *not* extensible

The following are defaults that a user can opt out of (by not calling the `With…` method)
but cannot reshape in place. The charter explicitly rejects configuration surface here:

- General help layout and per-command help layout. You can choose not to opt into help
  (register your own `[CliRoute("help")]`) but you can't override the default formatter
  via config. Ship a route that prints whatever you want.
- Error message wording. The framework writes errors to stderr in a fixed format. To
  reformat, inject a buffering `ICliConsole`, intercept writes, and reformat before
  forwarding.
- `CliApplication` is `sealed`. There is exactly one way to produce one:
  `CliApplication.Create(cfg => …)`. Extension happens through the contract (attributes)
  and the config, not through inheritance.

If you need something from this list, the right move is to propose a new opt-in
primitive that covers your scenario — not a hook that modifies the default.

---

## When you're tempted to add a new hook

Ask: *what real user, today, hits a wall without this?*

If the answer is hypothetical — "someone might want to customize X" — reject the
addition. Speculative extensibility bloats the public surface and creates decision
fatigue for every future user ("wait, which of these 12 hooks do I actually need?").

If the answer is concrete — a named scenario with a worked-out example — ask: *is this
best expressed as a hook (runtime override), or as a new opt-in primitive (a new
bundle, a new attribute subclass, a new helper)?* Primitives compose; hooks don't.
Prefer primitives unless the scenario truly requires runtime indirection.
