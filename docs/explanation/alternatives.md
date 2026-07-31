# The alternatives, honestly

**Landscape checked: 2026-07-29.** Everything below is a claim about someone else's project, so it
carries a date. If you are reading this much later, re-check before believing it — and if you find it
stale, that is a bug worth an issue. It has been one twice, which is why the notice is here.

Portico is one of six reasonable choices for a .NET CLI, and for many projects it is not the right
one. This page says which, and why. A comparison that concedes nothing is not worth reading.

## Where things stand

Versions re-pulled from nuget.org on 2026-07-29.

| Framework | Version | Shape | Reflection? |
|---|---|---|---|
| [System.CommandLine](https://learn.microsoft.com/dotnet/standard/commandline/) | **2.0.10 (GA)** | builder + lambdas | yes |
| [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) | 5.7.13 | source generator | **no** |
| [CliFx](https://github.com/Tyrrrz/CliFx) | 3.0.0 | attributed command classes | **no** |
| [Cocona](https://github.com/mayuki/Cocona) | 2.2.0 — **archived 2025-12-14** | attributed methods | yes |
| [Spectre.Console.Cli](https://github.com/spectreconsole/spectre.console.cli) | 0.55.0 — own repository | attributed settings classes | yes |
| Portico | 0.x | attributed methods | yes |

Two rows moved since the last check, and both change more than a number.

**Spectre.Console.Cli was extracted into its own repository** and now versions independently — 0.55.0
against Spectre.Console 0.57.2. Anything reading `spectreconsole/spectre.console` for CLI behaviour
is reading the wrong tree; the claims on this page were re-verified against the new one.

**CliFx 3.0.0 replaced reflection with source generators.** Command types must now be declared
`partial` so the generator can extend them, `Main()` is generated, and it advertises Native AOT and
trimming compatibility. `BindingConverter<T>` became `IInputConverter<T>`. This is a rewrite, not an
increment.

That makes **two of the five source-generator based**, not one — so "reflection-based" is now the
minority position among actively-developed alternatives, and this page should not be read as
implying otherwise. It does not change [the reflection-first decision](aot.md), which rests on the
operational .NET-system niche rather than on generators being rare: Portico accepts managed startup
cost to compose the application's assembly graph. A comparison table moving is not new evidence
about that trade.

## The architectural question before the feature checklist

All six can parse a command line. The useful question is what role the CLI is meant to play.

| If you primarily need... | Start with... |
|---|---|
| Microsoft's general-purpose parser and first-party stewardship | System.CommandLine |
| Native AOT, minimal startup, and generated dispatch | ConsoleAppFramework or CliFx |
| Rich terminal presentation | Spectre.Console.Cli, often with Spectre.Console |
| A small attribute-based application after Cocona | ConsoleAppFramework, CliFx, or Portico, depending on the constraints below |
| An operational entry point compiled with a substantial .NET system | Portico |

Portico's architectural bet is that an operational CLI can be another adapter over the same typed
application and domain assemblies as the production engine. A contract assembly can carry routes,
domain-specific option attributes, executable examples, and middleware policy; an implementation
assembly can call the same application services used elsewhere; a small executable composes them.
The compiler then checks that assembly graph as one .NET system.

That is a different optimization target from a standalone utility. It favors runtime composition,
ordinary DI, and semantic density over generated dispatch and the smallest possible binary. It also
creates a responsibility: referencing an assembly must not become an excuse to bypass an intentional
service or authorization boundary. [Why Portico?](why-portico.md) states both the proposition and
that limit in full.

## What each one is better at than Portico

**System.CommandLine — if institutional safety is the constraint.** It is Microsoft's, it reached
**2.0 GA in November 2025**, and the old "perpetual beta" criticism no longer applies. The 2.0
release also cut library size by ~32%, cut the
NativeAOT baseline app size by ~20%, improved startup ~12% and parsing ~40% against beta4. If your
organisation's rule is "prefer the first-party option," that rule is defensible here.

Portico's bet against it is not stability and not performance. It is **composition and shape**:
System.CommandLine gives an application a capable syntax tree and handler API; Portico makes the
typed command contract, reusable attribute vocabulary, middleware policy, and referenced assembly
graph the primary units of composition. A builder-and-lambda tree is also what people say drove them
off System.CommandLine. From the Spectre.Console community, on why they migrated away — they wanted
*"a simple programming style rather than the complex fluent style with nested lambdas that the
library favored"*, said it *"became more annoying to use over time"*, and wanted *"very predictable
debugging"*
([spectre.console discussion #1397](https://github.com/spectreconsole/spectre.console/discussions/1397)).
That is a citation, not our taste. It is one reason Portico declares routes with attributes on
methods.

Worth knowing if you are coming from an attribute-binding framework: System.CommandLine 2.0 GA'd with
`System.CommandLine.Hosting` and `System.CommandLine.NamingConventionBinder` **deprecated on NuGet**.
The attribute binder and the first-party DI story are precisely the parts it does not currently give
you.

**ConsoleAppFramework — if startup time, allocation or binary size is the constraint.** It is a
source generator: zero reflection, zero allocation, NativeAOT. It will beat Portico on every one of
those numbers and we are not going to pretend otherwise. Portico instead uses reflection and
`DispatchProxy` as the composition model for a larger managed application graph
([the reflection-first decision](aot.md), including its costs and revisit conditions).

**Spectre.Console — if the output is the product.** Tables, progress bars, colour, live displays.
Portico does not own presentation and does not want to. Compose them: Azure Functions Core Tools
ships on System.CommandLine *plus* Spectre.Console, which is exactly the split we think is right —
a router that routes, and a renderer that renders. Spectre also has genuine testing support:
`CommandAppTester` — a documented, first-class helper with assertions on `ExitCode`, `Output` and
`Settings` (the typed parse result). `CliTestHarness` covers similar ground (exit code, stdout,
stderr, stdin injection); the main addition is `CliContractValidator<T>` (automated contract
verification). Portico's harness serializes its own runs while it redirects the process-global
console, so unrelated parallel tests that touch `Console` still need a non-parallel collection.

**CliFx — if you like the attributed-command-class shape and want AOT with it.** It does not drag
`Microsoft.Extensions.*` behind it, and since 3.0.0 it is source-generated, so it competes with
ConsoleAppFramework on trimming and Native AOT while keeping a shape much closer to Portico's than
System.CommandLine's. If the attributed shape is what you want *and* AOT is a constraint, it is the
closest thing to Portico that satisfies both — and Portico does not.

This page used to call CliFx "small, stable and mature". That predated the 3.0.0 rewrite and
undersold it.

**Cocona — for maintaining an existing application, not starting a new one.** It was archived by
its author on **14 December 2025**. It was the closest thing to Portico's shape, and if you are
maintaining one, we wrote you a
[migration guide](../how-to/migrate-from-cocona.md) that is explicit about when ConsoleAppFramework
or System.CommandLine is the better destination instead.

## Four smaller operational differences

These are not the architectural pitch, but they matter in unattended and production-adjacent tools.
The comparison is a dated source survey, not a timeless claim of exclusivity.

A survey of all six repositories on **2026-07-29** (cloned and grepped, not read from their
documentation; evidence recorded on POR-95) found none of the following anywhere:

| Capability | Competitors with it |
|---|---|
| Secret redaction — a sensitive option's **value** withheld from errors and help | **0 of 6** |
| Human-readable durations (`"30 seconds"`, `90s`, `PT30S`) | **0 of 6** |
| Immutable collection binding (`ImmutableArray<T>` and friends) | **0 of 6** |
| Named POSIX exit-code constants | **0 of 6** |

**Secret redaction is the difference worth stating plainly.** In the dated survey, every compared
framework interpolated the user's value into parse-error text. System.CommandLine's template,
verified in its `Resources.resx` on 2026-07-29:

```
Cannot parse argument '{0}' for option '{1}' as expected type '{2}'.
```

`{0}` is what the user typed. Spectre additionally re-renders the whole command line with a caret
marker under the offending token.

The problem was named in
[command-line-api#1191](https://github.com/dotnet/command-line-api/issues/1191) — *"The environment
variable is expanded and shown in the help. This might be an issue if the environment variable is
being used on sensitive options like `--password`"* — opened **5 February 2021** and **still open**
(checked 2026-07-29). The suggested `IsSecure` was never built. The near-misses that do exist
(`Option.Hidden`, `[Hidden]`, `IsHidden`, `[HideDefaultValue]`) hide the *option*, never the *value*.

Portico's `Sensitive = true` withholds the value everywhere the framework echoes argv, and
`--help` names an option's environment variable without ever reading it.

### What we do *not* claim about signals

An earlier working assumption in this project was that mapping SIGTERM to exit 143 is unowned. **That
is false**, and it is worth recording why, because it is the kind of claim that feels true.

**System.CommandLine implements it properly and by name.** Its `ProcessTerminationHandler` defines
`SIGINT_EXIT_CODE = 130` and `SIGTERM_EXIT_CODE = 143` and registers both through
`PosixSignalRegistration`, on by default. DotMake inherits it verbatim.

The narrower statement, which is the defensible one: *an unconditional signal-to-exit-code mapping
that works on the synchronous path and distinguishes SIGTERM from SIGINT.* Three things separate it —
System.CommandLine's handler is built only for asynchronous actions, so a synchronous `int Run()`
gets nothing; 130 and 143 there are forced-termination codes applied when a handler misses a
two-second deadline, which is a race rather than a contract; and ConsoleAppFramework hooks SIGTERM
and then collapses it to 130, so an orchestrator cannot tell `docker stop` from Ctrl+C. Spectre
installs no signal handler at all, Cocona is internally inconsistent (Lite gives 130, Generic-Host
gives 0 on the same path), and CliFx exits 1 with a stack trace.

Also table stakes, and therefore not claimed anywhere: returning `int` as the exit code (6 of 6),
repeated-flag accumulation (5 of 6), `T[]` / `List<T>` / `IEnumerable<T>` binding (5 of 6), and
zero-registration custom type binding (5 of 6 — System.CommandLine is the outlier, with no
convention at all).

## The verification claim, stated precisely enough to be falsified

> **No other .NET CLI framework makes a command's documented examples executable tests of its own
> routing.**

Note what that does *not* say.

It does **not** claim Portico is the only library with compile-time diagnostics. Several competitors
ship them, and they are good:

- **ConsoleAppFramework** — CAF001–CAF018, all `DiagnosticSeverity.Error`, generated at compile time
  by its source generator. Includes duplicate command names, invalid global-options types, and a
  doc-comment-to-parameter name check that is structurally the same idea as POR001.
- **CliFx 3.x** — 18 descriptors (16 Error, 2 Warning), packed at `analyzers/dotnet/cs`. Includes
  `CommandOptionMustHaveUniqueName` and `CommandOptionMustHaveUniqueShortName` — duplicate-alias
  detection, the same check as POR009.
- **DotMake.CommandLine** — DMCLI01–DMCLI42, mixed Error/Warning. Accessibility, constructors,
  bindability, and parent/child wiring.

Portico's twelve rules ([POR001–POR013](../../README.md); POR007 retired) cover
overlapping but different ground.
POR001/POR005 (route-placeholder binding) and POR004 (a `[CliRoute]` with no `[CliCommandExample]`)
have no counterpart in the others, because they follow from the attribute-routing model and the
contract-validation mechanism. The others — duplicate routes, malformed option specs, unconvertible
types — are variations of checks the competitors also make. The difference is model, not count.

And `[CliCommandExample]` is not a comment: `CliContractValidator<T>` runs every example through the
real pipeline and fails the test suite when one stops dispatching, or stops binding the value it used to
bind.

Composition is not claimed as an invention: mounting sub-CLIs is
[not novel](../how-to/compose-clis.md) — oclif and cobra have done it for years. Portico's proposition
is the particular combination of .NET assembly composition, contract-first routes, extensible
operational attributes, middleware policy, and verification that survives the mount.

**Declarative attribute routing is not part of the claim either**, because it is table stakes: clap
derive (Rust), picocli (Java), typer (Python), kong (Go) and oclif (Node) all have it. Nor do we claim
the shortest program in every comparison. Code generation makes typing framework wiring cheap.
Reading it, reviewing it, debugging it, and keeping it coherent across teams are not free. Portico
therefore aims for **low accidental ceremony and high semantic density**: a route should mostly state
the operational contract, while reusable attributes and middleware carry repeated domain meaning.

## Who got there first

An honest pitch names its prior art. **"Nobody thought to validate examples" would be false**, and we
are not going to say it.

- **Spectre.Console.Cli** — the nearest .NET prior art. `ConfiguratorExtensions.ValidateExamples()`
  sets `Settings.ValidateExamples = true`; `CommandModelValidator.ValidateExamples` runs a real
  `CommandTreeParser(model, settings.CaseSensitivity, ParsingMode.Strict)` over each
  `.WithExample(...)`. (Re-verified 2026-07-29 in `spectreconsole/spectre.console.cli`, the repository
  it was split into — the claim survived the move unchanged.) It is **opt-in**, runs at
  **application startup** (not build time), and checks
  **parsing** — an example with an unrecognised option fails, but `--count abc` against an `int Count`
  passes because type conversion is not part of the check. Anyone saying Spectre examples are "just
  help text" is wrong. The honest distinction: Spectre checks tokenization; Portico checks dispatch and
  binding.
- **Nushell** — the strongest prior art in any ecosystem. `fn examples()` on the `Command` trait
  carries `example`, `description` and `result: Option<Value>`. `example_support.rs` evaluates the
  example and asserts against the declared result; each command file carries its own `#[test] fn
  test_examples()`. Beyond that it has `check_example_input_and_output_types_match_command_signature`
  and `check_all_signature_input_output_types_entries_have_examples` — **a coverage gate that fails
  when a declared signature variant has no covering example.** Portico has no equivalent of that
  coverage gate. Honest limits: in-process pipeline evaluation against a `Value`, not argv-level
  dispatch, and Nushell is a shell/plugin SDK rather than a general CLI framework. (Verified in source,
  2026-07-27.)
- **Azure CLI** — `azdev linter` carries `faulty_help_example_parameters_rule`, which parses every help
  example against the real command parser — but with `_check_value` and `_get_value` **mocked out**
  (type conversion deliberately disabled). (Checked in `azdev/operations/linter/rules/help_rules.py`,
  2026-07-27.) It proves an example's option names are *recognised*, not that their values are valid.
  The honest distinction: Microsoft built bespoke tooling for *one* CLI; Portico makes it the
  framework's central abstraction.
- **[trycmd](https://github.com/assert-rs/trycmd)** (Rust) executes README examples as snapshot tests —
  the closest thing to this idea in a mainstream ecosystem.
- **docopt** attacked the same drift from the opposite end, deriving the parser *from* the help text.

What survives all five: **no .NET CLI framework checks that a declared example dispatches to a specific
handler and binds specific values, as a build-time gate.** Spectre checks tokenization at startup if
you ask; azdev checks recognition with conversion mocked out; Nushell executes examples against an
in-process pipeline evaluation. Portico runs each example through the real dispatch pipeline against a
`DispatchProxy` of the contract interface, and a stale one fails the test suite — retyping an option from
`int` to `string` breaks even though the example still parses.

That is the precise verification claim. The broader reason to choose Portico is architectural: you
want the CLI to be a typed operational boundary inside a larger .NET system, and you value its
assembly composition, domain vocabulary, policy bundles, and reflection-first runtime model. If that
is not your problem, one of the frameworks above is probably a better choice.
