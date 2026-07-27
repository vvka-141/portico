# The alternatives, honestly

**Landscape checked: 2026-07-14.** Everything below is a claim about someone else's project, so it
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
deliberately — an admin CLI running inside a service's container does not care about a 36 ms startup
delta ([the AOT decision](aot.md), and what would change our mind).

**Spectre.Console — if the output is the product.** Tables, progress bars, colour, live displays.
Portico does not own presentation and does not want to. Compose them: Azure Functions Core Tools
ships on System.CommandLine *plus* Spectre.Console, which is exactly the split we think is right —
a router that routes, and a renderer that renders.

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

Portico's ten rules ([POR001–POR010](../../README.md)) cover overlapping but different ground.
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

- **Azure CLI** got there first, and did it properly. Its `azdev linter` carries
  `faulty_help_example_parameters_rule`, which parses every help example **through the real command
  parser** and fails CI on one that does not hold up. (Checked in `azdev/operations/linter/rules/help_rules.py`
  — not taken on trust.) The honest distinction is scope, not novelty: Microsoft built bespoke tooling
  for *one* CLI, checking that an example's options are **recognised**. Portico makes it the framework's
  central abstraction, and checks that an example **dispatches to a specific handler and binds specific
  values** — so retyping an option from `int` to `string` fails the build even though the example still
  parses.
- **[trycmd](https://github.com/assert-rs/trycmd)** (Rust) executes README examples as snapshot tests —
  the closest thing to this idea in a mainstream ecosystem.
- **docopt** attacked the same drift from the opposite end, deriving the parser *from* the help text.

What survives all three: no .NET CLI framework makes verified examples the contract, and no framework
in any ecosystem checks that an example reaches the handler it names, binding the values it names.

That is the entire pitch. If none of it is worth anything to you, one of the frameworks above is a
better choice, and you should use it.
