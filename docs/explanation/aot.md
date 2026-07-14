# Portico — AOT considerations (deferred)

> **Status.** Deferred. Not on the 1.0 roadmap. This document explains why, what the triage
> criteria would be if we revisit, and what the minimal-investment path would look like. Do
> not start implementation work without re-reading [the decision log](#decision-log) at the
> bottom of this file and confirming the stated conditions still hold.

---

## 1. The decision

**Portico 1.0 ships without AOT support.** The runtime uses reflection for route
discovery, option binding, and help rendering — exactly as it does today. Consumers who publish
with `PublishAot=true` will see trim warnings and the framework will not work correctly under a
trimmed AOT binary.

This is a deliberate choice, not a backlog item. The cost of building a source generator for AOT
would be weeks of implementation plus indefinite dual-maintenance (every new feature has to land
in both the reflection path and the generator emission); the benefit would be parity with a
feature gap that the framework's actual target users do not have.

## 2. Who AOT matters for — and doesn't

**AOT matters** when a CLI is:
- Invoked many times per shell session (shell completion wrappers, git-like tools).
- Distributed globally as a standalone binary (`dotnet tool install -g` competing against `go install` / `cargo install`).
- Running in cold-start-sensitive environments (Lambda, serverless, HEALTHCHECK scripts).

**AOT does not matter** when a CLI is:
- A microservice entrypoint that runs once per container start. The container takes seconds to
  become healthy; saving 130 ms of .NET startup is invisible.
- An admin / configuration tool embedded in a larger .NET application. The runtime is already
  loaded; the assembly is already on disk.
- A developer productivity tool inside a .NET monorepo, where `dotnet run` or `dotnet <tool>` is
  the invocation shape.

The framework's initial target audience — .NET shops building internal tooling and microservice
entrypoints — is almost entirely the second list.

## 3. Why not "do it anyway, just in case"

Three concrete costs:

1. **Dual implementation.** Every time a new attribute, parameter shape, or type converter is
   added, it has to be implemented twice — once for the reflection path, once for the generator
   emission — until reflection is removed entirely. Removing reflection is a breaking change for
   any consumer who hasn't opted into AOT.
2. **Source-generator maintenance burden.** Roslyn source generators are not free. They break on
   C# language updates, produce duplicate symbols under multi-project references, add
   1–3 s to incremental build times, and require Roslyn-literate maintenance. A typical .NET
   developer cannot effectively debug a misbehaving generator.
3. **No current user pain.** No user (including the framework's author) has asked for AOT. The
   only driver for the feature was a speculative "what would a Microsoft reviewer say" critique.
   That critique is useful for framing the work, but it is not a user need.

## 4. Market reality check

At the time of writing, **no mainstream .NET CLI ships as AOT.**

- `dotnet` itself is a native bootloader, but every `dotnet-*` sub-tool (`dotnet-ef`,
  `dotnet-format`, `dotnet-aspnet-codegenerator`) is reflection-based.
- The widely-used CLI ecosystem (`gh`, `kubectl`, `terraform`, `aws`) is Go or Python — .NET is
  not the language of choice for *distributable* CLIs.
- ASP.NET Core itself did not get AOT support until .NET 8 in 2023 — seven years after 1.0.

A developer who prioritizes AOT binary size / startup speed today has more reason to pick Go or
Rust than to wait for Portico to add AOT. The framework's wedge is *.NET developer ergonomics*
for .NET shops — not *portable binary distribution* for cross-ecosystem competition.

## 5. Triage criteria — when to revisit

Reopen this decision if **any two** of the following become true:

1. Multiple (3+) users file concrete issues asking for AOT support with specific scenarios.
2. A user contributes a working source-generator prototype as a pull request.
3. The `dotnet tool install` distribution pattern becomes the primary way Portico CLIs are
   consumed (evidence: NuGet download telemetry shows this).
4. A competing .NET CLI framework (Cocona, Spectre.Console.Cli) ships AOT first and starts
   winning adopters who cite AOT as the reason.

Until then, the cost/benefit does not pencil out.

## 6. The minimal-investment path, if ever needed

Not a plan. An outline, in case future-us or a contributor picks this up.

### Option A — Trim annotations only, no generator

Add `[DynamicDependency]` and `[RequiresUnreferencedCode]` attributes at the reflection call
sites so the trimmer preserves what's needed, and users get clear warnings when they attempt
AOT. This is a **1–2 session** change with no generator complexity:

- `CliApplication.AddCommands(object instance)` — annotate with `[RequiresUnreferencedCode]`.
  User sees a warning; user annotates their own types; AOT build succeeds.
- `CliMiddleware` ctor, `CliOptionsParameterInfo.Materialize` — same treatment.
- `TypeDescriptor.GetConverter(Type)` in `CliOptionAttribute.CanAccept` — hardest case; may
  require a static type-converter registry for the primitives the framework natively supports.
- Publish one sample csproj demonstrating an AOT-clean Portico CLI with user-side annotations.

This delivers **functional AOT for the 80% case** (scalar + flag options over primitive types)
without shipping a generator. Users who want more (custom converters, reflection-heavy bundle
ctors) either stay on the reflection build or contribute the annotations themselves.

### Option B — Full source generator

A full source generator replacing every reflection hot-path with generator output is the
larger alternative. Estimated weeks of implementation, indefinite dual-maintenance, and a
meaningful IDE/build-speed tax. Only pursue if Option A is demonstrably insufficient for a
concrete user.

## 7. What consumers should know today

If a consumer tries `PublishAot=true` on a Portico app, they'll see trim warnings
and the binary will likely misbehave at runtime (routes not discovered, options not bound). The
framework's documentation does not claim AOT support. If this becomes a blocker for a real
scenario, file an issue describing the use case — the decision above is reversible.

---

## Decision log

- **2026-04-20** — decided to defer AOT indefinitely. The original prescriptive plan is replaced
  by this considerations document. Driver: framework author's own use cases (dockerised
  microservice CLI entrypoints) don't require AOT; no concrete external demand; source-generator
  maintenance burden doesn't pencil out against the benefit. Reopen when §5 triage criteria
  firm up.
