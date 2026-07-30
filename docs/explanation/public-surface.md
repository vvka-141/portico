# Portico — Exported Public Surface

> **Purpose.** A deliberate, recorded review of every type the framework exports.
> Filed as POR-104; the companion test `Portico_PublicSurface_Should` enforces the
> list — adding or removing an exported type fails the build until this document is
> updated.
>
> **Last reviewed:** 2026-07-27 (0.x, pre-release).

---

## Classification key

| Tag | Meaning |
|-----|---------|
| **primitive** | Intended consumer-facing API. Documented, supported, stable within a major version. |
| **detail** | Implementation detail that should be `internal`. Made internal as part of this review. |
| **undecided** | Ships public in 0.x under SemVer's "anything may change" licence. Revisit before 1.0. |

---

## Portico (core package)

### Framework entry point and builder

| Type | Kind | Tag | Reason |
|------|------|-----|--------|
| `CliApplication` | sealed class | primitive | The entry point. `CliApplication.Create(...)` is the first line of every Portico program. |
| `ICliApplicationBuilder` | interface | primitive | Fluent builder API consumed in the `Create` lambda. |
| `CliVersionBuilder` | sealed class | primitive | Exposed by `ICliApplicationBuilder.WithVersion(Action<CliVersionBuilder>)`. |
| `CliHelpBuilder` | sealed class | primitive | Exposed by `ICliApplicationBuilder.WithHelp(Action<CliHelpBuilder>)`. |

### Attributes (the declarative surface)

| Type | Kind | Tag | Reason |
|------|------|-----|--------|
| `CliRouteAttribute` | sealed class | primitive | Route decoration — every handler method carries one. |
| `CliOptionAttribute` | class | primitive | Option declaration. Unsealed: virtual seams (`CanAccept`, `GetValueComparer`, `AllowsCsv`). |
| `CliArgumentAttribute` | class | primitive | Positional argument declaration. Unsealed: virtual `CanAccept`. |
| `CliCommandExampleAttribute` | sealed class | primitive | Example declaration — the wedge. |

### Runtime types

| Type | Kind | Tag | Reason |
|------|------|-----|--------|
| `CliFlag` | readonly record struct | primitive | Presence-only option type. |
| `CliOptions` | abstract class | primitive | Bundle base class for grouped option properties. |
| `CliMiddleware` | abstract class | primitive | Cross-cutting option + lifecycle hook base. |
| `CliInvocation` | sealed class | primitive | Parsed invocation passed to middleware and handlers. |
| `ICliConsole` | interface | primitive | Console abstraction for output redirection and testing. |
| `SystemCliConsole` | sealed class | primitive | Default `ICliConsole` singleton. Naming conflict with `Cli*` convention noted on POR-12. |
| `CliPrompt` | static class | primitive | Interactive prompt helpers. |
| `CliExitException` | sealed class | primitive | Exit-code signalling exception. |
| `CliConfigurationException` | sealed class | primitive | Thrown from `CliApplication.Create` on invalid configuration. |

### Ready-made middleware

| Type | Kind | Tag | Reason |
|------|------|-----|--------|
| `CliTimingMiddleware` | sealed class | primitive | `--timing` wall-clock output. |
| `CliTracingMiddleware` | sealed class | primitive | `--trace-level` bridge to `System.Diagnostics.Trace`. |

### Shell completion (`Portico.Completion`)

| Type | Kind | Tag | Reason |
|------|------|-----|--------|
| `CliCompletion` | static class | primitive | Completion script generation. |
| `CliCompletionShell` | enum | primitive | Target shell for completion scripts. |

### Option captures (`CliInvocation.Options`)

These types model the parsed command-line tokens. Public because `CliInvocation.Options`
returns `ImmutableArray<CliOptionCapture>` and consumers pattern-match on the concrete shapes.

| Type | Kind | Tag | Reason |
|------|------|-----|--------|
| `ICliOptionCapture` | interface | primitive | Root capture contract — `Name`. |
| `CliOptionCapture` | abstract record | primitive | Base record all six concrete captures inherit. |
| `CliScalarOptionCapture` | sealed record | primitive | `--opt value` |
| `CliFlagOptionCapture` | sealed record | primitive | `--flag` (no value) |
| `CliCollectionOptionCapture` | sealed record | primitive | `--opt a b c` |
| `ICliCollectionCapture` | interface | primitive | Groups scalar + collection captures for uniform materializer access. |
| `ICliMapOptionCapture` | interface | primitive | Groups keyed captures. |
| `CliKeyValueOptionCapture` | sealed record | primitive | `--cfg[key] value` |
| `CliKeyFlagOptionCapture` | sealed record | primitive | `--cfg[key]` (presence-only keyed) |
| `CliKeyCollectionOptionCapture` | sealed record | primitive | `--cfg[key] a b c` |

### Testing (`Portico.Testing`)

Ships inside the core package — the differentiator.

| Type | Kind | Tag | Reason |
|------|------|-----|--------|
| `CliTestHarness` | sealed class | primitive | In-process test runner. |
| `CliTestRunResult` | sealed record | primitive | Captured exit code + stdout + stderr. |
| `CliContractValidator<T>` | sealed class | primitive | The wedge — compile-time example verification. |
| `CliContractExample` | sealed record | primitive | Extracted example metadata for the validator. |
| `CliTestAssertionException` | sealed class | primitive | Assertion failure from contract validation. |

**Core total: 36 types.**

---

## Portico.DependencyInjection (adapter package)

| Type | Kind | Tag | Reason |
|------|------|-----|--------|
| `CliApplicationBuilderExtensions` | static class | primitive | `AddCommands<T>(this ICliApplicationBuilder)` overloads resolving from `IServiceProvider`. |

**DI total: 1 type.**

---

## Portico.Hosting (adapter package)

| Type | Kind | Tag | Reason |
|------|------|-----|--------|
| `CliHostExtensions` | static class | primitive | `RunCli(this IHost)` integration. |

**Hosting total: 1 type.**

---

## Summary

**38 exported types across 3 assemblies. All classified primitive.**

No types were found to be accidental exports — the extraction from Solitons already
internalized all reflection helpers, string extensions, materializers, and route-model
types. The public surface is the contract surface.

### Deferred to 1.0

A `PublicApi.Shipped.txt` analyzer baseline (Microsoft.CodeAnalysis.PublicApiAnalyzers)
is 1.0 work — noted in `docs/ROADMAP.md`. The companion test
`Portico_PublicSurface_Should.Track_every_exported_type_by_name` serves as the 0.x gate.

---

## Member-level surface (POR-83 §2)

The audit above stops at types. A type can be correctly public and still expose members that are not
part of anyone's contract, and none of those were visible to a type-level pass. This section records
the verdict for each, and the two rules the verdicts follow.

**Why this is alignment, not tidying.** `CliApplication` is `sealed`, the materializer seam is
[SEALED](../ROADMAP.md) (POR-36), and `Portico_Extensibility_Should` fails the build if a fifth
inheritable type appears. Portico's extensibility story is deliberately narrow. A public surface of
framework plumbing contradicts that story — the point is to make the members agree with the types.

### Rule 1 — a `System.Reflection` type in a signature means the member is the pipeline's

An attribute author never holds a `ParameterInfo`: inside `[CliOption("--x")]` there is nothing to get
one from. A member that demands one can only be called by the framework, so it should not be public.

Enforced by `Portico_PublicSurface_Should.Keep_Reflection_Typed_Members_Off_The_Public_Surface`, which
has **no allow-list** — adding an exception is a deliberate edit, not a default.

| Member | Verdict | Reason |
|--------|---------|--------|
| `CliOptionAttribute.IsOptional(ParameterInfo, out object?)` | **internal** | Rule 1. Resolves the parameter-binding path's default. |
| `CliOptionAttribute.IsOptional(PropertyInfo, out object?)` | **internal** | Rule 1. Resolves the bundle-property path's default. |
| `CliArgumentAttribute.References(ParameterInfo)` | **internal** | Rule 1, and worse than merely unusable — see below. |
| `CliCommandExampleAttribute.Get(MethodInfo)` | **deleted** | Dead. See below. |

**Neither `IsOptional` overload is redundant** — POR-83 asked, and the answer is no. They differ in the
one argument that decides the outcome: a parameter has a reflected default (`int tail = 100`), a
property has none, so `hasReflectedDefault` is `true` on one path and always `false` on the other.
Collapsing them would silently grant bundle properties a default they cannot have, and these two paths
have drifted before (POR-59).

**`References` was a trap, not just plumbing.** It compares against `ParameterName`, which the pipeline
fills in during discovery — so called from outside the framework it returns `false` for *every* real
parameter. A member whose only possible answer is the wrong one.

**`CliCommandExampleAttribute.Get` was dead.** Zero callers repo-wide; its own doc claimed "used by the
help renderer and contract validator" and both read `GetCustomAttributes` directly. Deleted outright
per CLAUDE.md's no-deprecation rule — the same shape and the same fix as POR-101's
`CliOptionAttribute.Get`. **It was not on POR-83's list**: it turned up by enumerating the surface
instead of reading the audit, which is the argument for the test above existing at all.

**What stays public, and why the rule is not "narrow everything".** `IsMatch(string)`,
`CanAccept(Type, out TypeConverter)` and `GetValueComparer()` take a `string` or a `Type` — values a
subclass genuinely holds. `CliOptionAttribute` and `CliArgumentAttribute` *are* documented extension
points; the rule narrows what nobody can call correctly, not what nobody happens to call today.

### Rule 2 — a settable property the framework populates is a mutation seam

| Member | Verdict | Reason |
|--------|---------|--------|
| `CliArgumentAttribute.ParameterName` | **`internal set`** (getter unchanged) | Resolved by reflection. Every user write is wrong: before discovery it is overwritten, after discovery it breaks the binding just resolved. POR-70 removed the `(parameterName, description)` constructor for this reason; the setter was the same capability by the back door. |
| `CliArgumentAttribute.Name` | **keep public set** | The *display* form (`Name = "PATH"`). An attribute named argument requires a settable property, and this one is the user's choice. |
| `CliTimingMiddleware.Timing`, `CliTracingMiddleware.Level` | **keep public set** | A `CliOptions` bundle binds by `SetValue`, and a middleware *is* a bundle. The setter is the binding mechanism. |

Enforced by `Portico_PublicSurface_Should.Allow_Only_The_Documented_Mutable_Properties`, which is an
exact set in both directions. It reads the `IsExternalInit` modreq to exclude `init` accessors: a naive
`SetMethod.IsPublic` check reports twenty properties, seventeen of them records, and buries the three
that matter.

### Default interface implementations — **keep**, and the concern does not apply

POR-83 §2 asked whether the DIMs on `ICliApplicationBuilder` should become extension methods, on the
grounds that they "cannot be removed without a breaking change" and "make the interface
non-implementable-in-full by design". Measured rather than assumed, and the verdict is keep:

- **They are `sealed` DIMs**, so they are **not virtual** — confirmed by reflection, `IsVirtual` is
  `false` for all five. An implementer cannot override them and is never asked to. The
  non-implementable-in-full objection describes an *unsealed* DIM; these behave like extension methods
  that happen to live on the interface.
- **The removal objection is not an argument for the change** — an extension method cannot be removed
  without a breaking change either.
- **There is exactly one implementation**, `CliApplication.Builder`, and it is `private sealed`. There
  are no third-party implementers to burden, and none are invited. (A doc comment in
  `Portico.DependencyInjection` claimed to serve "custom `ICliApplicationBuilder` implementations";
  it was describing an internal middleware's synchronous hook, and has been corrected.)
- **Overload coherence is a real cost.** `WithVersion` has five overloads and `AddCommands` five.
  Splitting some onto an extension class puts instance and extension methods in one overload set,
  where the instance always wins and resolution differences get subtle. One set in one place is the
  more defensible design.

`ICliConsole`'s three DIMs (`IsColorEnabled`, `IsInputRedirected`, `IsOutputRedirected`) *are* virtual,
and that is also correct: users do implement `ICliConsole` — the docs invite a test console — and none
of them should be forced to answer whether colour is enabled.
