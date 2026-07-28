# The alternatives, honestly

**Landscape checked: 2026-07-27.** Everything below is a claim about someone else's project, so it
carries a date. If you are reading this much later, re-check before believing it — and if you find it
stale, that is a bug worth an issue.

Portico is one of five or six reasonable choices for a .NET CLI, and for many projects it is not the
right one. This page says which, and why. A comparison that concedes nothing is not worth reading.

## Where things stand

| Framework | Version | Shape | Reflection? |
|---|---|---|---|
| [System.CommandLine](https://learn.microsoft.com/dotnet/standard/commandline/) | **2.0.9 (GA)** | builder + lambdas | yes |
| [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) | 5.7.13 | source generator | **no** |
| [CliFx](https://github.com/Tyrrrz/CliFx) | 3.0.0 | attributed command classes | yes |
| [Cocona](https://github.com/mayuki/Cocona) | 2.2.0 — **archived 2025-12-14** | attributed methods | yes |
| [Spectre.Console.Cli](https://spectreconsole.net/) | with Spectre.Console | attributed settings classes | yes |
| Portico | 0.x | attributed methods | yes |

## What each one is better at than Portico

**System.CommandLine — if institutional safety is the constraint.** It is Microsoft's, it reached
**2.0 GA in November 2025**, and the old "perpetual beta, do not build on it" line is dead — do not
let anyone (including us) sell it to you. The 2.0 release also cut library size by ~32%, cut the
NativeAOT baseline app size by ~20%, improved startup ~12% and parsing ~40% against beta4. If your
organisation's rule is "prefer the first-party option," that rule is defensible here.

Portico's bet against it is not stability and not performance. It is **shape**: a builder-and-lambda
tree is what people say drove them off it. From the Spectre.Console community, on why they migrated
away — they wanted *"a simple programming style rather than the complex fluent style with nested
lambdas that the library favored"*, said it *"became more annoying to use over time"*, and wanted
*"very predictable debugging"*
([spectre.console discussion #1397](https://github.com/spectreconsole/spectre.console/discussions/1397)).
That is a citation, not our taste. It is also the whole reason Portico declares routes with
attributes on methods.

Worth knowing if you are coming from an attribute-binding framework: System.CommandLine 2.0 GA'd with
`System.CommandLine.Hosting` and `System.CommandLine.NamingConventionBinder` **deprecated on NuGet**.
The attribute binder and the first-party DI story are precisely the parts it does not currently give
you.

**ConsoleAppFramework — if startup time, allocation or binary size is the constraint.** It is a
source generator: zero reflection, zero allocation, NativeAOT. It will beat Portico on every one of
those numbers and we are not going to pretend otherwise. Portico uses reflection and `DispatchProxy`
deliberately — an admin CLI running inside a service's container does not care about a startup
delta that is invisible next to the container start it lives inside ([the AOT decision](aot.md),
and what would change our mind).

**Spectre.Console — if the output is the product.** Tables, progress bars, colour, live displays.
Portico does not own presentation and does not want to. Compose them: Azure Functions Core Tools
ships on System.CommandLine *plus* Spectre.Console, which is exactly the split we think is right —
a router that routes, and a renderer that renders. Spectre also has genuine testing support:
`CommandAppTester` — a documented, first-class helper with assertions on `ExitCode`, `Output` and
`Settings` (the typed parse result). `CliTestHarness` covers similar ground (exit code, stdout,
stderr, stdin injection); the main additions are `CliContractValidator<T>` (automated contract
verification) and the process-global console gate that prevents parallel-test interference.

**CliFx — if you like the attributed-command-class shape** and want something small, stable and
mature that does not drag `Microsoft.Extensions.*` behind it.

**Cocona — nothing, any more.** It was archived by its author on **14 December 2025**. It was the
closest thing to Portico's shape, and if you are stranded there, we wrote you a
[migration guide](../how-to/migrate-from-cocona.md) that is explicit about when ConsoleAppFramework
or System.CommandLine is the better destination instead.

## The one claim we make, stated precisely enough to be falsified

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

Portico's ten rules ([POR001–POR011](../../README.md), POR007 retired) cover overlapping but
different ground.
POR001/POR005 (route-placeholder binding) and POR004 (a `[CliRoute]` with no `[CliCommandExample]`)
have no counterpart in the others, because they follow from the attribute-routing model and the
contract-validation mechanism. The others — duplicate routes, malformed option specs, unconvertible
types — are variations of checks the competitors also make. The difference is model, not count.

And `[CliCommandExample]` is not a comment: `CliContractValidator<T>` runs every example through the
real pipeline and fails the build when one stops dispatching, or stops binding the value it used to
bind.

Composition is not part of the claim, deliberately: mounting sub-CLIs is
[not novel](../how-to/compose-clis.md) — oclif and cobra have done it for years. What is unusual is
that Portico's verification survives the mount.

**Declarative attribute routing is not part of the claim either**, because it is table stakes: clap
derive (Rust), picocli (Java), typer (Python), kong (Go) and oclif (Node) all have it. And "less
boilerplate" is not a benefit worth selling in 2026 — an agent will emit two hundred lines of builder
wiring and never get bored. Anyone pitching you brevity is pitching a scarcity that no longer exists.

## Who got there first

An honest pitch names its prior art. **"Nobody thought to validate examples" would be false**, and we
are not going to say it.

- **Spectre.Console.Cli** — the nearest .NET prior art. `ConfiguratorExtensions.ValidateExamples()`
  sets a flag; `CommandModelValidator.ValidateExamples` runs a real `CommandTreeParser(model,
  caseSensitivity, ParsingMode.Strict)` over each `.WithExample(...)`. (Verified in source,
  2026-07-27.) It is **opt-in**, runs at **application startup** (not build time), and checks
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
`DispatchProxy` of the contract interface, and a stale one fails the build — retyping an option from
`int` to `string` breaks even though the example still parses.

That is the entire pitch. If none of it is worth anything to you, one of the frameworks above is a
better choice, and you should use it.
