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

The [Charter §6 / §6.5](explanation/charter.md) gates are the checklist. **Two of them are currently
not met** and say so, with ticket numbers — a gate nobody re-runs is decoration.

---

## Open

### The one unresolved API decision

- **C4 — `CliOptionMaterializer` extensibility.** Expose `WithMaterializer<T>(...)`, or seal the
  base. Carried unresolved from the origin's roadmap. **Must be settled before 1.0** — tracked as
  **POR-36**.

  Why it cannot wait: *exposing* extensibility later is additive and safe; *removing* it later is a
  breaking change. Ship 1.0 with an accidentally-public materializer seam and we are committed to
  supporting it forever. The 0.x window is the only cheap time to decide.

  The Charter's own YAGNI rule (§7: "speculative extensibility is rejected") points at **seal**, and
  the BCL already answers "my type is not convertible from a string" with `[TypeConverter]` — an
  idiomatic extension point that costs the framework nothing.

### Everything else

Open work lives in the tracker (project `POR`), not here. This file exists to carry the decisions a
roadmap is uniquely good at recording: what is deliberately *not* being built, and why.

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
  fallback is the line we draw.

- **Plugin-style command loading from external assemblies.** A security and boundary problem, with no
  asking user.

- **Markdown / man-page emission.** A separate package post-1.0, if ever.

- **Option/value-level shell completion.** Verb-level completion ships and is host-wired; the deeper
  pieces stay parked until a concrete user need appears. Simplicity-first against the
  backend-entrypoint audience.

- **Spectre-style rich rendering.** A composition story, not a Portico concern. Charter §5.

---

## Not carried across from the origin

The origin's roadmap was largely a session-by-session delivery plan (its Sessions 12–16), and every
item in it had shipped by the time of the extraction. That is *history*, and it is the origin's
history — Portico's begins at commit `a17854b`. It is not reproduced here.

What *was* carried is what a roadmap is actually for: the open decision (C4) and the parked list
above — the reasoning that stops a future session re-litigating a settled question.
