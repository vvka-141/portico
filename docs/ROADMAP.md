# Portico — Roadmap

> **Status.** Living document. What is *open*, what is *parked*, and what 1.0 means.
> **Audience.** Maintainers. The design constitution is [the Charter](explanation/charter.md).

Portico is 0.x. SemVer's 0.x licence ("anything may change") **is** the preview channel — there are
no alpha/beta feeds. 1.0 is cut when the API is one we would defend, not when the code is done.

---

## What 1.0 means — the three-axis test

A 1.0 release ships when all three are true:

1. **Correct** — no known silent-misbehaviour bugs. A wrong answer with exit code 0 is the one
   class of defect that must be zero.
2. **Conformant** — POSIX behaviour matches what users already expect from `git` / `docker` /
   `cargo`. Stream discipline, exit codes, `--`, short-option gluing, `NO_COLOR`.
3. **Differentiated** — the shape-defining choices (attribute routing, examples-as-tests, map
   options, container-agnostic dispatch, shipped analyzers) are sharpened, not eroded.

The [Charter §6 / §6.5](explanation/charter.md) gates are the checklist. **The two that were not met
are now met** — analyzer coverage (POR-25: POR009 + POR010 close it) and XML docs on the public
surface (POR-27), and the second is now enforced by a test rather than by an audit. A gate nobody
re-runs is decoration, so before you trust this paragraph, re-run them.

---

## Open

*(No open API decisions. C4 — the last one carried over from the origin — is resolved below.)*

### Everything else

Open work lives in the tracker (project `POR`), not here. This file exists to carry the decisions a
roadmap is uniquely good at recording: what is deliberately *not* being built, and why.

---

## Resolved decisions

### C4 — `CliOptionMaterializer` extensibility: **SEALED**. Resolved 2026-07-14 (POR-36).

The question, carried unresolved from the origin: expose a `WithMaterializer<T>(...)` seam, or seal
the base? **Sealed.** `CliOptionMaterializer` stays `internal`. There is no `WithMaterializer<T>`,
and a test (`Portico_PublicSurface_Should.NotExposeTheOptionMaterializerSeam`) fails if one appears.

**Why.** The extension points a user actually needs already exist, and they cost the framework
nothing:

1. **`[TypeConverter]`** — the BCL's own answer to *"my type is not convertible from a string"*. A
   user's domain type binds as a `[CliOption]` today with no framework seam at all:

   ```csharp
   [TypeConverter(typeof(SemVerConverter))]
   public sealed record SemVer(int Major, int Minor, int Patch);

   [CliRoute("ship")]
   [CliCommandExample("ship --version 2.1.4")]
   int Ship([CliOption("--version")] SemVer version);   // binds. verified, not assumed.
   ```

   `CliOptionAttribute.CanAccept` resolves it via `TypeDescriptor.GetConverter`. It is an extension
   point every .NET developer already knows, and one we did not have to invent, document or maintain.

2. **Subclassing `CliOptionAttribute` / `CliArgumentAttribute`** and overriding `CanAccept` — for the
   rarer user who needs to change converter *selection* itself.

Charter §7 is directive: *"YAGNI applies hard… Speculative extensibility is rejected. If we proposed
a hook once and nobody asked for it again, delete the proposal; don't ship the hook."* Nobody has hit
a wall these two points cannot solve.

**The asymmetry is what makes this the safe call.** Exposing a seam later is additive and backward
compatible. Removing one is a breaking change. So the cost of waiting is zero and the cost of
guessing wrong is permanent — wait for a named scenario.

**To reopen:** name the concrete user scenario that `[TypeConverter]` and a `CliOptionAttribute`
subclass together cannot express. That, not a hypothetical, is the bar.

### Making `CliExitException` unswallowable at run time: **NO**. Resolved 2026-07-29 (POR-145).

A `catch (Exception)` in a handler swallows `CliExitException`, so a failed command can exit 0 —
verified, and the worst failure this framework's audience can have, since a CI step or a deployment
gate reads the exit code and nothing else. The obvious wish is to make the exception impossible to
swallow. **It cannot be done, and it should not be attempted.**

There is no CLR mechanism that makes a managed exception uncatchable. `catch (Exception)` and a bare
`catch` take everything; the special-cased types (`StackOverflowException`, the removed
`ThreadAbortException`) are runtime-privileged and not constructible by a library. Corrupted-state
exception filtering does not exist on .NET Core, and never applied to ordinary exceptions anyway.

Two workarounds were considered and rejected:

1. **`AppDomain.CurrentDomain.FirstChanceException`** — fires before any handler, so the framework
   could observe "an exit was requested" and override the returned code. It is process-global, it
   fires for exceptions that are *legitimately* caught (a retry loop, a nested `CliApplication.Run`,
   a test asserting a throw), and it costs something on a hot path for a purely diagnostic signal.
2. **An `AsyncLocal` "exit requested" flag** scoped to the invocation, checked when the handler
   returns normally. The same objection in milder form.

Neither can distinguish *swallowed by accident* from *caught on purpose*, and **overriding a
handler's returned exit code from an ambient side channel is worse than the bug.**

The answer is [POR013](reference/analyzer-rules.md#por013), a build-time warning: it fails in the
author's editor rather than in production and costs nothing at run time. That is also the on-charter
answer — Portico's identity is *verified at compile time by Roslyn analyzers*, and a runtime
interception trick is exactly the magic the CHARTER's HTTP-metaphor test rejects.

**To reopen:** a CLR mechanism that did not exist in .NET 10. Not a cleverer side channel — the
objection is to the guess, not to the plumbing.

---

## Parked — explicitly deferred. Do not pick these up without revisiting the Charter.

- **AOT / source-generator path.** Deliberately deferred. See [aot.md](explanation/aot.md) for the
  full rationale and the triage criteria to reopen. Short version: the target users (internal
  tooling, backend service entrypoints) do not need AOT, and dual-maintaining reflection *plus* a
  generator is weeks of work without a concrete user scenario demanding it. Runtime reflection ships
  in 1.0; the minimal-investment path (trim annotations, no generator) is documented in `aot.md` §6
  if demand ever firms up.

  **This is the single most re-litigated decision in the project's history. Read `aot.md` before
  reopening it.**

- **Interactive prompts beyond `GetYesNoAnswer`.** Composition with Spectre.Console is the answer.

- **Configuration-file fallback (`appsettings.json`).** A CLI's input is argv. Environment-variable
  fallback is the line we draw — and since POR-54 it works on scalars, flags and collections, and
  refuses maps out loud rather than binding nothing.

- **Response files (`@args.txt`).** Declined (POR-55). `csc`, `dotnet` and `curl` all expand a leading
  `@`, and the argument for it is real: a backfill command with a thousand ids outgrows the shell's
  length limit, and that is Portico's audience.

  It is declined anyway, because **the same job is done better by a `--ids-file path` option that the
  handler reads**: that option appears in `--help`, and — the deciding point — it is *verifiable by an
  example* (`[CliCommandExample("db backfill --ids-file ids.txt")]`). Response-file expansion happens
  **before routing**, so it is invisible to the contract: no example can cover it, no analyzer can
  check it, and the CLI's own description of itself would silently omit a way of invoking it. A
  framework whose central claim is "the examples are the contract" cannot ship an input channel the
  contract cannot see.

  It also fails the Charter's own test — it has no expression in the HTTP metaphor. There is no
  `@file` in a query string.

  **Reopen if:** a real user hits the command-line length limit and a `--*-file` option genuinely does
  not serve them. That is new evidence, and it beats this reasoning.

- **Plugin-style command loading from external assemblies.** A security and boundary problem, with no
  asking user.

- **Markdown / man-page emission.** A separate package post-1.0, if ever.

- **Option/value-level shell completion.** Verb-level completion ships and is host-wired; the deeper
  pieces stay parked until a concrete user need appears. Simplicity-first against the
  backend-entrypoint audience.

- **Spectre-style rich rendering.** A composition story, not a Portico concern. Charter §5.

- **`PublicApi.Shipped.txt` analyzer baseline.** `Microsoft.CodeAnalysis.PublicApiAnalyzers`
  is the standard tool for API-break detection; deferred to 1.0 because the surface is
  still moving. The 0.x gate is `Portico_PublicSurface_Should.Track_every_exported_type_by_name`
  (POR-104) — a name-list assertion that fails the build when the exported set changes.

---

## Not carried across from the origin

The origin's roadmap was largely a session-by-session delivery plan (its Sessions 12–16), and every
item in it had shipped by the time of the extraction. That is *history*, and it is the origin's
history — Portico's begins at commit `a17854b`. It is not reproduced here.

What *was* carried is what a roadmap is actually for: the open decision (C4) and the parked list
above — the reasoning that stops a future session re-litigating a settled question.
