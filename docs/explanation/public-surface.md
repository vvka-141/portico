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
