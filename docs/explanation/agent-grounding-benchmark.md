# Does shipping the guide help? A measured answer

**Run 2026-07-24. Result: a cold agent goes from 0/20 to 20/20 when the package's own guide is in
context.** This page states the method, the raw result, and — at least as important — the four claims
this experiment does **not** support.

## Why the experiment exists

Portico has effectively zero presence in any model's training data. System.CommandLine has ~89M
downloads and a decade of Stack Overflow answers. If agents increasingly author code, a new framework
is structurally disadvantaged: the network effect shifts from "what humans have heard of" to "what is
in the weights."

The proposed counter was to ship Portico's own instruction asset inside the NuGet package
(`PORTICO-FOR-AGENTS.md`, POR-50), on the theory that an agent does not only rely on priors — it reads
what you put in front of it. **That was a theory. This measures it.**

## Method

Two arms, twenty command-line specifications, forty trials.

| | arm A | arm B |
|---|---|---|
| Given | the spec + "use the Portico framework" | the same, **plus** the shipped `PORTICO-FOR-AGENTS.md` |
| Documentation | none | the guide, and nothing else |

- **Subjects: forty independent agents with clean context**, one per trial. None had seen Portico.
  Tool use was audited per trial: arm A performed **0** tool calls (no file access at all); arm B
  performed **exactly 1** (reading the guide from an isolated copy — no repository access).
- **Specs** span the ordinary surface: options, subcommands, positional and optional-positional
  arguments, presence-only flags vs two-state booleans, repeated/collection options, map options,
  durations, enums, defaults, aliases, async handlers, exit codes, option bundles, environment
  fallback, and three-level routes.
- **Analyzers were active in both arms** — the real `dotnet add package Portico` experience.
- **Grading is mechanical. No human or model judgement enters it:**
  - *first-pass compile* — `dotnet build` exit code, no retries, no feedback loop.
  - *dispatch correctness* — each spec fixes an exact output line; the built binary is executed with
    the golden invocation and its **stdout and exit code are compared verbatim**. Because the spec
    fixes the output rather than the identifiers, the grade is independent of whatever the agent
    chose to name its types and methods.
- The grader was validated in both directions before any trial was run: known-good code passes,
  deliberately-broken code fails.

Raw data is published alongside this page so the numbers can be checked rather than taken on trust:

- [`agent-grounding-benchmark-results.jsonl`](../reference/agent-grounding-benchmark-results.jsonl) —
  one line per trial: compile outcome, every diagnostic code, and each golden invocation with its
  expected and actual stdout and exit code.
- [`agent-grounding-benchmark-specs.json`](../reference/agent-grounding-benchmark-specs.json) — the
  twenty specifications and their golden invocations, exactly as given to the agents.

The harness and the generated programs live in `.private/spikes/por42-run/` (untracked).

## Result

| | arm A (no guide) | arm B (guide) |
|---|---|---|
| first-pass compile | **0 / 20** (0%) | **20 / 20** (100%) |
| dispatch correctness | **0 / 20** (0%) | **20 / 20** (100%) |

Two-proportion z = 6.32, two-sided p = 2.5 × 10⁻¹⁰, for both measures. Not one arm-A trial compiled;
not one arm-B trial failed.

### What the cold agents invented

Arm A did not produce near-misses. It produced fluent, confident code against an API that does not
exist — `CliApplication.Create<T>(instance)`, `cli.Map<T>()`, `config.UseCommandsFrom(...)`,
`config.AddService(...)`, `[CliFlag]` as an attribute, `[CliRequired]`. Failure codes across arm A:

| code | n | what it was |
|---|---|---|
| `POR008` | 10 | handler returned `void` — Portico's own analyzer catching it |
| `CS1061` | 8 | a method that does not exist on the builder |
| `CS1729` | 4 | the two-argument `[CliArgument(name, desc)]` — a constructor **removed in POR-70** |
| `CS0308` | 4 | wrong generic arity |
| `CS0592` | 2 | `[CliFlag]` used as an attribute; it is a type |
| `CS0616`, `CS0246` | 2 | attribute/type that does not exist |

The `CS1729` cases are the sharpest illustration: two independent cold agents reinvented a
constructor shape Portico deleted the day before. Plausible-looking API guessing is not a near miss —
it is a different framework.

## What this does NOT show

Read this section before quoting the numbers.

1. **It is not a claim that Portico is easier for agents than any other framework.** There is no
   competing framework in this experiment. Arm A's failure is substantially a *zero-corpus-presence*
   effect — a fact about Portico's novelty, not about its design. A model writes System.CommandLine
   from memory because it has seen it, not because builders are clearer than attributes.
2. **It says nothing about "declarative attributes suit LLMs."** That claim remains unmeasured and
   must not appear on any public surface (CHARTER §7, POR-44). This experiment varies *documentation*,
   holding the framework constant. It cannot speak to API shape.
3. **Single model family.** All forty subjects were Claude agents with clean context. No cross-vendor
   replication was available, so generalisation to other models is untested.
4. **Complete separation caps what the statistics can say.** 0/20 versus 20/20 means the effect is
   "very large" but not precisely estimable, and it implies the spec set does not straddle the
   difficulty threshold. Finding where grounding *stops* helping needs harder specs — that is the
   useful follow-up, not a larger N at this difficulty.

A fifth, minor caveat: the specs were authored by the same agent that wrote the guide. They are
ordinary CLI shapes rather than a curated showcase, but an independent spec set would be stronger.

## The defensible claim

> Shipping the framework's own guide inside the package takes a cold agent from **producing nothing
> that compiles** to **producing correct, dispatching code on the first attempt**, across twenty
> command-line specifications.

That is a statement about *grounding*, and it is the entire justification for POR-50 shipping
`PORTICO-FOR-AGENTS.md` in the `Portico` package rather than leaving it in the repository. It is not a
competitive claim, and it should never be written as one.

## Still unmeasured

The **diagnostic-quality** arm (POR-49): does analyzer message quality change an agent's first-pass
*fix* rate? It could not be measured here, because grounded agents made almost no mistakes for the
analyzers to catch — arm B needs a deliberately mistake-provoking spec set to fire POR00x at all.
Notably, `POR008` fired ten times in arm A, so the analyzers do work on hallucinated code; those
trials simply failed to compile for other reasons too.
