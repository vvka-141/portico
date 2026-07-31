# Analyzer rules

Portico ships Roslyn analyzers **inside the package**. One `dotnet add package Portico` and your
build starts checking your CLI — no separate analyzer package, no configuration. Every rule below is
decidable from your declarations alone; that is the test for whether a rule belongs here at all.

**Nine structural rules have a runtime backstop.** `CliApplication.Create` re-checks invalid route,
option and handler shapes and throws `CliConfigurationException` on startup. POR004 is an authoring-
discipline rule: refusing to start a valid command because its example is missing would turn a
documentation gap into an outage. POR012 and POR013 diagnose legal but risky code, so neither has an
invalid runtime shape to reject. The table makes the boundary explicit.

**Seven of the twelve ship a code fix**, so Ctrl-. clears the diagnostic. All seven support *Fix all in
document / project*, which matters most after a rename: one parameter rename can produce a dozen
POR001s at once. The other five deliberately offer nothing — in each case the correction needs a decision
the analyzer cannot see, and **a code fix that guesses is worse than no code fix, because you accept it
without reading.** Each rule below says which it is and why.

| Rule | Severity | What it catches | Runtime backstop | Code fix (Ctrl-.) |
|---|---|---|---|---|
| [POR001](#por001) | Error | A `{placeholder}` in a route matches no parameter | Yes | Rename, or add the parameter |
| [POR002](#por002) | Error | Two methods **on one type** declare the same route | Yes | — |
| [POR003](#por003) | Error | A malformed `[CliOption]` alias spec | Yes | Partial — see below |
| [POR004](#por004) | Error | A `[CliRoute]` with no `[CliCommandExample]` | No — authoring discipline | Insert an example stub |
| [POR005](#por005) | Error | `[CliArgument]` has no matching route placeholder | Yes | Add the placeholder to the route |
| [POR006](#por006) | Error | A `CliOptions` bundle with no public parameterless constructor | Yes | Insert the constructor |
| [POR008](#por008) | Error | A `[CliRoute]` method that cannot return an exit code | Yes | — |
| [POR009](#por009) | Error | Two options on one command declaring the same alias | Yes | — |
| [POR010](#por010) | Error | A `[CliOption]` type that cannot be built from a command-line string | Yes | — |
| [POR011](#por011) | Error | A route declares the same `{placeholder}` twice | Yes | — |
| [POR012](#por012) | Warning | A `[CliOption]` on a `bool` is probably meant to be a switch | No — legal code | Change to `CliFlag?` |
| [POR013](#por013) | Warning | A `catch` clause in a handler swallows `CliExitException` | No — legal code | Add the `when` filter |

---

## POR001

**Route placeholder does not match any parameter.**

```csharp
[CliRoute("deploy {target}")]           // ← {target}
int Deploy(string environment) => 0;    // ← but the parameter is 'environment'
```

A `{name}` token in a route is bound to the parameter of the same name. Rename the placeholder, or
rename the parameter. There is nothing else the framework could bind it to.

**Code fix:** offers one **Rename placeholder** action per parameter the method declares, plus
**Add parameter `string {name}`**. Both remedies are legitimate and which one is right is your call, so
both are offered rather than one being presumed. A method with no parameters gets only the add.

## POR002

**Two methods on the same type declare the same route.**

One type is one command surface, so a repeated route there is unambiguous — one of the two methods
can never be reached.

**The rule is scoped to the declaring type on purpose.** Two *different* contracts that each declare
`status` are a legal program: they may be [mounted](../how-to/compose-clis.md) under different root
routes (`storage status` and `queue status` never collide), or your application may register only one
of them. The analyzer can see neither the mount nor the registration, so it says nothing — and the
runtime, which sees both, still rejects a genuine collision at `CliApplication.Create`.

**Code fix:** none. Which of the two methods should keep the route, and what the other one's route
should become, is not visible to the analyzer.

## POR003

**Malformed `[CliOption]` spec.**

```csharp
[CliOption("verbose")]     // ← no dashes: this is not an alias
[CliOption("--a b")]       // ← whitespace inside an alias
[CliOption("--x|")]        // ← trailing pipe
```

The spec is a pipe-separated alias list: `"--verbose"`, `"--verbose|-v"`, `"-v"`.

**Code fix:** partial, and deliberately so. Offered for the two shapes that carry unambiguous intent —
an undashed alias (`"verbose"` → `"--verbose"`, or `"-v"` for a single character) and an empty segment
from a leading, doubled or trailing pipe (`"--verbose|"` → `"--verbose"`). For `""`, a whitespace-only
spec, `"-"` or `"--"` **no action is offered**: there is no name to recover, and a guess you accept
without reading is worse than the error. The repair is re-validated before it is offered, so it can
never hand back a spec this rule still reports.

## POR004

**A `[CliRoute]` with no `[CliCommandExample]`.**

An example is not a comment. `CliContractValidator<T>` runs every one of them through the real
pipeline, so a route without an example is a route nothing tests — and a command your users have no
worked invocation for.

It is an **Error**, and that is deliberate. This rule flags a missing *test* rather than a broken
program, which is the usual argument for warning severity — but examples-are-tests is the one
invariant Portico asks you to accept, and an invariant enforced at Warning holds only in projects
that happen to set `TreatWarningsAsErrors`. A code fix ships with it, so the fix is one keystroke:
accept the stub, then replace the placeholder text with the command as a user would type it.

If you genuinely want a route with no example, suppress it the ordinary way (see below) — that is a
deliberate, visible, per-route decision rather than a rule that quietly does nothing.

## POR005

**`[CliArgument]` has no matching route placeholder.**

```csharp
[CliRoute("copy {target}")]                          // ← declares {target} only
int Copy([CliArgument("where from")] string source,  // ← source has no placeholder
         string target) => 0;
```

A command's path is declared entirely by `[CliRoute]`, exactly as an ASP.NET Core route template
declares `{id}` inline. `[CliArgument]` describes an argument the route already declares — it
supplies the help text and display name and never adds a segment, so an argument the route does
not mention has no position to bind to. Put it in the route: `[CliRoute("copy {target} {source}")]`.

The mirror image of [POR001](#por001), which reports a placeholder with no parameter.

**Code fix:** appends the missing `{placeholder}` to the `[CliRoute]` string — one action, one
keystroke, build green. Appended last rather than inserted, because where in the path the argument
belongs is your decision and the end is the only position that cannot change the meaning of an existing
segment.

## POR006

**A `CliOptions` bundle needs a public parameterless constructor.**

The framework constructs a bundle per invocation with `Activator.CreateInstance`, so it cannot supply
constructor arguments.

**This does not apply to `CliMiddleware`**, even though it inherits from `CliOptions`. Middleware is
constructed by *you* and cloned per dispatch, never `Activator`-constructed — so a constructor
dependency is legitimate, and is exactly how a DI container injects into it.

**Code fix:** inserts the public parameterless constructor. Added alongside the existing one rather
than replacing it: a bundle may legitimately keep a convenience constructor for your own code to
call, and the framework only needs the parameterless one to exist.

## POR007 — retired

There is no POR007. It reported a parameter carrying two `[CliArgument]`s — a mistake that was
possible only because `CliArgumentAttribute` declared `AllowMultiple = true` and the framework then
banned what the attribute had just permitted. The attribute now declares `AllowMultiple = false`, so
the C# compiler reports it as **CS0579** before any analyzer runs: no package reference required, no
suppression path, nothing to disable.

The ID is not reused. The next free rule is POR014.

## POR008

**A `[CliRoute]` method must return `int` or `Task<int>`.**

A command's exit code is its result — `0` success, `1` runtime error, `2` usage error, `130`
cancelled. `void`, `async void` and non-generic `Task` cannot carry one, and `async void` is worse
than useless: an exception inside it crashes the process.

Throw `CliExitException` for error paths.

**Code fix:** none. Changing a return type means deciding what the handler should return, which is the
method's logic rather than a declaration.

## POR009

**Two options on one command declare the same alias.**

```csharp
int Deploy(
    [CliOption("--name")] string service,
    [CliOption("--name")] string cluster) => 0;    // ← both bind --name
```

Both would receive the same captured value at dispatch: silently shared state that almost never
matches intent. The rule covers direct parameters, the properties of a `CliOptions` bundle, and
collisions *between* the two — the case worth having a compiler for, since the two declarations live
in different files.

**Case follows the framework's rule:** a single-character short alias is case-**sensitive**, so `-v`
and `-V` are different options (the `curl -v` / `curl -V` idiom). Longer aliases are
case-**insensitive**, so `--name` and `--NAME` collide.

**Code fix:** none. Which of the two colliding options is the mistaken one is intent the analyzer
cannot see, and a fix that appended a suffix (`--name2`) would produce a compiling CLI with a nonsense
surface — worse than the build error.

## POR010

**A `[CliOption]` type that cannot be built from a command-line string.**

```csharp
public sealed class Money { public decimal Amount; }   // no TypeConverter

int Pay([CliOption("--money")] Money money) => 0;      // ← POR010
```

Everything a user types is text. Portico binds it through `TypeDescriptor`, so an option's type needs
a `TypeConverter` that converts from string. Give the type a `[TypeConverter]`, or use one that
already has a converter — a primitive, an enum, `string`, `TimeSpan`, `Guid`, `Uri`, `DateTime`, or a
collection or `string`-keyed map of those.

**The rule is deliberately conservative.** It fires only for a type declared in *your own* code,
because whether a *referenced* type has a converter is a runtime fact — `TypeDescriptor` carries an
intrinsic table Roslyn cannot see, and a converter can also arrive from a provider registered at
startup. At `Error` severity, a false positive would fail a build that works, which is strictly worse
than the runtime error it replaces. Where it cannot be certain, it stays silent and the runtime check
catches the rest.

**Code fix:** none. The fix is to write a `TypeConverter`, or to change the declared type to one that
already converts. Neither is mechanical.

## POR011

**A route declares the same `{placeholder}` twice.**

```csharp
[CliRoute("copy {p} {p}")]          // ← {p} appears twice
int Copy(string p) => 0;           // ← the second slot overwrites the first at dispatch
```

Both slots resolve to the same parameter. At dispatch the second value silently overwrites the first —
data loss that `CliContractValidator<T>` does not catch, making it a false green in the framework's
central verification mechanism. Use distinct placeholder names for distinct positions:
`[CliRoute("copy {src} {dst}")]`.

**Code fix:** none, for POR009's reason: which of the two repeated placeholders is the mistaken one,
and what it should be renamed to, is not something the analyzer can know.

## POR012

**A `[CliOption]` on a `bool` is probably meant to be a switch.** *(Warning)*

```csharp
[CliOption("--verbose")] bool verbose = false     // ← a user must type `--verbose true`
[CliOption("--verbose")] CliFlag? verbose = null  // ← `--verbose` on its own
```

`CliFlag?` is **presence-only**: the option is on by being there, and absent means off. A `bool` is a
two-state **value**, so it reads one — `--verbose` alone is not how it is used, and "absent" and
"false" collapse into the same answer.

This compiles cleanly and produces a CLI nobody can drive as intended, which is why it is a
diagnostic rather than a documentation note. Portico's own reference calls it
[the most common misuse in the framework](capabilities.md#cliflag-versus-bool--presence-versus-value).

`bool?` is reported too, with the same message. A three-state value is almost never what a command
line wants, and an author reaching for it is usually trying to express "absent" — which is what
`CliFlag?` already means.

**The code fix rewrites the declaration only.** Portico's contract normally lives on an interface
while the body lives on an implementing class, often in another file, so a fix that also rewrote
`if (verbose)` to `if (verbose is not null)` would be guessing at which implementation was meant.
Changing the declaration produces ordinary compile errors at exactly the sites that need attention —
the normal shape of a type-change refactor, and more honest than a partial rewrite that looks
complete.

### `bool` is still supported, and this is a Warning because of that

A genuine two-state value option — `--park-on-failure true` versus `--park-on-failure false`, where
the difference between "the author said no" and "the author said nothing" matters — is exactly what
`bool` is for. This rule cannot tell that case from the mistake, so it may not fail a build on its
own authority.

Suppress it the ordinary way when you meant the value:

```csharp
#pragma warning disable POR012
[CliOption("--force", "Cancel in-flight jobs instead of waiting")] bool force = false,
#pragma warning restore POR012
```

That example is real: `examples/ReferenceCli` carries two of them, because the rule fired on its
deliberate two-state options the first time it was built.

> **Know what Warning means here.** This repository and the `portico-cli` template both set
> `TreatWarningsAsErrors`, so in a scaffolded project POR012 *is* a build failure. That is deliberate
> rather than an oversight: `Info` severity is invisible in `dotnet build` and in CI, which is
> precisely where this mistake ships from. Meeting the distinction once, at the moment you write it,
> with a fix on the lightbulb, is the outcome the rule exists for.

## POR013

**A `catch` clause in a command handler swallows `CliExitException`.** *(Warning)*

```csharp
[CliRoute("migrate")]
[CliCommandExample("migrate")]
public int Migrate()
{
    try { throw new CliExitException("fatal: schema locked") { ExitCode = 17 }; }
    catch (Exception) { return 0; }     // ← exit 0. The migration failed.
}
```

`CliExitException` is the controlled-exit mechanism: the framework catches it at the application
boundary, writes its message to stderr and returns its `ExitCode`. A catch-all between the throw and
that boundary defeats it, and nothing reports that it happened.

This is the failure an operational CLI can least afford. A CI step, a Kubernetes job or a deployment
gate reads the exit code and nothing else — exit 0 from a failed migration is a green build over a
broken database. It is also invisible in review: `catch (Exception ex) { _log.Error(ex); return 1; }`
is ordinary defensive C# that a reviewer would wave through.

**Either fix works:**

```csharp
catch (Exception ex) when (ex is not CliExitException) { ... }   // let the exit through
catch (Exception) { throw; }                                     // rethrow it
```

The code fix writes the first, adding the `ex` identifier if the clause has none.

**Why this is a build error rather than a runtime guard.** There is no CLR mechanism that makes a
managed exception uncatchable, and every ambient workaround — `FirstChanceException`, an `AsyncLocal`
"exit requested" flag — cannot tell *swallowed by accident* from *caught on purpose*, which makes
overriding the handler's exit code on that guess worse than the bug. The decision and its reasoning
are recorded in [ROADMAP.md](../ROADMAP.md) so they are not re-proposed.

### What the rule does not see

**The handler body only.** A `CliExitException` thrown three frames deep and swallowed by a catch-all
in a helper class is out of reach — inter-procedural exception-flow analysis is not something this
suite attempts. The rule closes the common case, a defensive `try`/`catch` wrapped around the
handler's own work. It is not a guarantee that no controlled exit is ever swallowed.

**A clause with any `when` filter is left alone**, not only one that names `CliExitException`. A
filter means the author considered which exceptions the clause takes, and deciding in general whether
an arbitrary filter excludes a type is not worth a false positive.

A **type-level** `[CliRoute]` does not make a method a handler — it is a route prefix, and a method
without its own `[CliRoute]` is not reachable as a command. Only a method carrying `[CliRoute]`, or
implementing an interface method that carries it, is examined.

> **The framework itself does the thing this rule forbids**, at
> `CliApplication.SafeRunAsync`: a `catch (Exception e)` with no filter. It is correct there because
> it sits *after* the `catch (CliExitException)` arm — ordering protects it, not filtering. Inside a
> handler there is no such preceding arm, which is the whole difference.

---

## Suppressing a rule

These are ordinary Roslyn diagnostics. Suppress one the ordinary way — `#pragma warning disable
POR004`, an `.editorconfig` entry (`dotnet_diagnostic.POR004.severity = none`), or a
`[SuppressMessage]` attribute.

If you find yourself suppressing a rule routinely, that is worth an
[issue](https://github.com/vvka-141/portico/issues): either the rule is wrong, or it is badly
explained, and both are our bug rather than yours.
