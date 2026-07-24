# Analyzer rules

Portico ships Roslyn analyzers **inside the package**. One `dotnet add package Portico` and your
build starts checking your CLI — no separate analyzer package, no configuration. Every rule below is
decidable from your declarations alone; that is the test for whether a rule belongs here at all.

**Every rule has a runtime backstop.** The framework re-checks each of these at
`CliApplication.Create` and throws `CliConfigurationException` on startup. The analyzer does not
replace that check — it moves the failure into your edit loop, where you can fix it without running
anything.

| Rule | Severity | What it catches |
|---|---|---|
| [POR001](#por001) | Error | A `{placeholder}` in a route matches no parameter |
| [POR002](#por002) | Error | Two methods **on one type** declare the same route |
| [POR003](#por003) | Error | A malformed `[CliOption]` alias spec |
| [POR004](#por004) | Error | A `[CliRoute]` with no `[CliCommandExample]` |
| [POR005](#por005) | Error | `[CliArgument]` has no matching route placeholder |
| [POR006](#por006) | Error | A `CliOptions` bundle with no public parameterless constructor |
| [POR008](#por008) | Error | A `[CliRoute]` method that cannot return an exit code |
| [POR009](#por009) | Error | Two options on one command declaring the same alias |
| [POR010](#por010) | Error | A `[CliOption]` type that cannot be built from a command-line string |

---

## POR001

**Route placeholder does not match any parameter.**

```csharp
[CliRoute("deploy {target}")]           // ← {target}
int Deploy(string environment) => 0;    // ← but the parameter is 'environment'
```

A `{name}` token in a route is bound to the parameter of the same name. Rename the placeholder, or
rename the parameter. There is nothing else the framework could bind it to.

## POR002

**Two methods on the same type declare the same route.**

One type is one command surface, so a repeated route there is unambiguous — one of the two methods
can never be reached.

**The rule is scoped to the declaring type on purpose.** Two *different* contracts that each declare
`status` are a legal program: they may be [mounted](../how-to/compose-clis.md) under different root
routes (`storage status` and `queue status` never collide), or your application may register only one
of them. The analyzer can see neither the mount nor the registration, so it says nothing — and the
runtime, which sees both, still rejects a genuine collision at `CliApplication.Create`.

## POR003

**Malformed `[CliOption]` spec.**

```csharp
[CliOption("verbose")]     // ← no dashes: this is not an alias
[CliOption("--a b")]       // ← whitespace inside an alias
[CliOption("--x|")]        // ← trailing pipe
```

The spec is a pipe-separated alias list: `"--verbose"`, `"--verbose|-v"`, `"-v"`.

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

## POR006

**A `CliOptions` bundle needs a public parameterless constructor.**

The framework constructs a bundle per invocation with `Activator.CreateInstance`, so it cannot supply
constructor arguments.

**This does not apply to `CliMiddleware`**, even though it inherits from `CliOptions`. Middleware is
constructed by *you* and cloned per dispatch, never `Activator`-constructed — so a constructor
dependency is legitimate, and is exactly how a DI container injects into it.

## POR007 — retired

There is no POR007. It reported a parameter carrying two `[CliArgument]`s — a mistake that was
possible only because `CliArgumentAttribute` declared `AllowMultiple = true` and the framework then
banned what the attribute had just permitted. The attribute now declares `AllowMultiple = false`, so
the C# compiler reports it as **CS0579** before any analyzer runs: no package reference required, no
suppression path, nothing to disable.

The ID is not reused. The next free rule is POR011.

## POR008

**A `[CliRoute]` method must return `int` or `Task<int>`.**

A command's exit code is its result — `0` success, `1` runtime error, `2` usage error, `130`
cancelled. `void`, `async void` and non-generic `Task` cannot carry one, and `async void` is worse
than useless: an exception inside it crashes the process.

Throw `CliExitException` for error paths.

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

---

## Suppressing a rule

These are ordinary Roslyn diagnostics. Suppress one the ordinary way — `#pragma warning disable
POR004`, an `.editorconfig` entry (`dotnet_diagnostic.POR004.severity = none`), or a
`[SuppressMessage]` attribute.

If you find yourself suppressing a rule routinely, that is worth an
[issue](https://github.com/vvka-141/portico/issues): either the rule is wrong, or it is badly
explained, and both are our bug rather than yours.
