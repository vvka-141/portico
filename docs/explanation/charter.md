# Portico — Charter

> **Status.** Living document. Changes rarely. Every PR should be defensible against this charter.
> **Audience.** Maintainers and future contributors. This is the North Star — not marketing copy.

## 1. Mission

Give .NET developers the CLI framework that feels idiomatic to how they already write ASP.NET Core services.
A terminal tool should be designed, tested, and evolved the same way a controller is — because a CLI *is* an API;
the transport just happens to be argv instead of HTTP.

## 2. Target user

A .NET developer (intermediate to senior) who:
- Has written ASP.NET Core controllers or minimal APIs.
- Has tried existing .NET CLI frameworks and found a shape mismatch — wanted attribute routing,
  examples-as-tests, map options, or freedom from `Microsoft.Extensions.*` coupling.
- Wants to stand up a production CLI in under a day and have it be testable, themeable, and refactor-safe.

## 3. The metaphor (non-negotiable)

A CLI is an HTTP API without the H.

| HTTP                          | Portico                              |
| ----------------------------- | ------------------------------------------------- |
| `[Route("api/projects/{id}")]`| `[CliRoute("projects get")]`                      |
| Route parameter `{id}`        | `[CliArgument(nameof(id), …)]`                    |
| `[FromQuery] string format`   | `[CliOption("--format")]`                         |
| `?cfg[env]=prod`              | `--cfg[env] prod` (map option)                    |
| `[FromBody] RequestDto`       | `CliOptions`                                 |
| `IActionFilter`               | `CliMiddleware.OnExecuting/Executed`      |
| Action selector ranking       | `CliMethodInfo.RankByOptions`                     |
| Integration tests             | `CliContractValidator<T>` (examples *are* tests)  |

**Rule.** If a feature is hard to explain via the HTTP analogy, that's evidence it probably doesn't belong.

## 4. Invariants (things we do not compromise)

1. **Attributes over builders.** Routes, args, options are declared with attributes on methods and parameters.
   No fluent `.AddCommand(c => c.WithSubcommand(...))` trees.
2. **Examples are tests.** `[CliCommandExample("init . --template basic")]` is executable documentation validated
   by `CliContractValidator<T>` via `DispatchProxy`. This is the signature feature.
3. **Map options (`--cfg[key] value`) are first-class.** They parse cleanly into `IDictionary<string, T>`.
4. **Interface-first contracts are supported.** You can decorate an interface, validate examples, then implement —
   OpenAPI-for-CLI.
5. **Stable from 1.0.** Semver applies from the 1.0 cut forward; surface drift before then is contained to active initiatives and announced in the changelog.
6. **Dependency policy is a hard rule — the core has ZERO dependencies.** `Portico` ships with the
   BCL and nothing else. Not `System.Reactive`, not `System.Linq.Async`, and above all **no
   `Microsoft.Extensions.*`** (no DI, no Logging, no Configuration, no Options, no Hosting) — ever.
   When a feature would naturally reach for one of those, Portico expresses the same capability
   container-agnostically (factory delegates, generic resolvers, argv-as-input).

   The MEDI adapters live in **separate packages** — `Portico.DependencyInjection` and
   `Portico.Hosting` — precisely so the core can stay clean. That separation is what makes the
   zero-dependency claim credible rather than merely inconvenient for the backend teams we target.

   This is not aspiration: it is asserted by a test (`Portico_Package_Should.HaveNoDependencies`,
   which walks the built assembly's referenced assemblies), and the produced `.nupkg` carries an
   empty dependency group. A stray `PackageReference` fails the build, not a review.

   > **This is a deliberate tightening.** The library Portico was extracted from permitted
   > `System.Reactive` + `System.Linq.Async` in its core. Portico permits neither. Zero means zero.
7. **Richness without stiffness.** The framework grows by *opt-in primitives*, never by
   *forced behaviors*. A user who doesn't opt into a feature should not pay for it — in
   code volume, in surface area, or in learning overhead. Concretely:
   - Defaults are "off" for anything ambitious. `WithVersion` must be explicitly called;
     silence is the default. Presentation (logos, descriptions, colors) is outside the
     framework's scope — users print their own banners before `Run()` or from a
     dedicated route.
   - `CliApplication` stays `sealed`. One way to extend: implement the contract (interface
     decorated with attributes), configure via `ICliApplicationBuilder`. Two extensibility
     dimensions (inheritance *and* config) create confusion and a public contract that is
     hard to evolve. If a user hits a wall config cannot solve, that's a signal to add an
     opt-in primitive — not to unseal.
   - **YAGNI applies hard.** A hook, a renderer override, a custom handler — each is added
     only when a real user today hits a wall without it. Speculative extensibility is
     rejected. If we proposed a hook once and nobody asked for it again, delete the
     proposal; don't ship the hook.
   - The handler contract is sacred. A CLI action is a **plain C# method** that uses
     `Console.Write*` for output, throws `CliExitException` (or returns an `int`) for
     control flow, and expects its options bound by the framework. The framework never
     injects extra plumbing into handler code.
   - Minimalism is about *what is forced on users*, not about *feature count*. A library
     with 20 opt-in convenience bundles and 2 required concepts is more minimalist than
     one with 2 bundles and 5 hooks that every user has to understand.

## 5. Non-goals

- **Not a Spectre.Console replacement.** We don't own presentation (tables, progress bars, prompts beyond Y/N).
  Users can compose with Spectre.Console for rendering — we stay focused on routing + binding + contract testing.
  This split is not a cop-out: **Azure Functions Core Tools** ships on System.CommandLine *plus*
  Spectre.Console — a first-party CLI using a router and a renderer together, exactly as prescribed here.
- **Not a REPL / interactive shell framework.** One command, one invocation, one exit code.
- **No custom DSL file formats _as input_.** No `.cli.json` schema that *defines* the CLI and
  competes with C# as the source of truth. C# is the schema. **Emitting the command surface as
  machine-readable _output_ is permitted** (POR-41, decided 2026-07-15). A manifest derived from the
  attributes does not compete with "C# is the schema" — it *is* that schema serialized, and it
  cannot drift because there is no second source of truth. The boundary is hard: read-only emission
  of the surface Portico already models, no schema *ingestion*, no MCP server mode, no plugin
  loading. The moment an emitted artefact is read back to *alter* behaviour it has become input, and
  this permission does not cover it.
- **No attempt at Windows-only features by default.** POSIX-friendly first; Windows-idiomatic bits are opt-in.

## 6. Success metrics for 1.0

- Every `NotImplementedException` on the hot path is gone. (Holds: there is not one in `src/`.)
- ✅ **`dotnet new portico-cli` scaffolds a runnable project in under 30 seconds** (POR-23, shipped as
  `Portico.Templates`). It scaffolds more than "runnable": the project builds with zero warnings under
  the analyzers, and its `CliContractValidator<T>` test is **already green**. That is the point — the
  template's job is to put the examples-are-tests loop in front of a new user before they have read a
  word of documentation. A CI job scaffolds from the packed template and builds and tests the result on
  every push, so this gate cannot rot.
  **Not yet decided** — whether to ship a template at all is an open question, tracked as POR-23.
  If the answer is "no template", this metric is struck rather than left as an aspiration nobody owns.
- AOT is explicitly out of scope for 1.0 — the framework uses reflection for route
  discovery, option binding, and help rendering. See [aot.md](aot.md) for the deferral
  decision and its revisit conditions; it is the single source of truth on AOT.
- A user can write a testable command with 1 method, 1 attribute, 1 example — no plumbing.
- The README's first code block tells the whole story in under 20 lines.

## 6.5. Agent-first release gate

Portico is an agent-first library. Its public surface is the primary place agents read attributes,
generate controllers, and pattern-match from examples. Every agent-hostile affordance that ships
unfixed becomes an agent-training problem downstream. Before any 1.0 release, the following are hard
gates, not nice-to-haves.

> **These gates are claims about the code, and claims rot.** Two of them were inherited from the
> origin marked "✅ audited" and were **false for Portico when checked in July 2026**. A gate nobody
> re-runs is decoration. Where a gate is not met, it now says so and names the ticket.

- **✅ Analyzer coverage — complete for every attribute contract (POR-25).** It was inherited marked
  "✅ audited" and was false; the two gaps it hid are now closed.
  Shipping today: `[CliRoute]` → POR001 (placeholder mismatch), POR002 (duplicate route), POR008
  (invalid return type); `[CliArgument]` → POR005 (unknown parameter), POR007 (duplicate argument);
  `[CliOption]` → POR003 (malformed spec), **POR009 (duplicate alias)**, **POR010 (type that cannot
  be built from a command-line string)**; `[CliCommandExample]` → POR004 (missing example); and the
  `CliOptions` **bundle** constructor contract → POR006.

  Every one of these is *decidable from the declaration alone*, which is the test for whether a rule
  belongs here at all. Each has a runtime backstop at `CliApplication.Create` for builds without the
  analyzer — the analyzer moves the failure into the edit loop, it does not replace the check.

  **POR010 is deliberately conservative, and that is not a gap.** Whether an arbitrary *referenced*
  type has a `TypeConverter` is a runtime fact — `TypeDescriptor`'s intrinsic table is invisible to
  Roslyn — so the rule fires only for a type declared in the user's own compilation. At `Error`
  severity a false positive would fail a build that works, which is strictly worse than the runtime
  error it replaces. Silence where it cannot be certain is the correct behaviour, not a shortfall.

  **POR006 does NOT cover `CliMiddleware`** (POR-26). Middleware is user-constructed and cloned via
  `MemberwiseClone`, never `Activator.CreateInstance`d, so a constructor dependency is legitimate —
  it is exactly how a DI container injects into it. The rule previously conflated the two lifecycles
  because `CliMiddleware` inherits from `CliOptions`, and in doing so forbade the ordinary DI shape.

  There is no `[CliFlag]` attribute to analyze (`CliFlag` is a `readonly record struct`, a flag-arity
  *type*), so **no `[CliFlag]` analyzer is warranted**. The one adjacent agent-hostile choice — using
  `bool` (a two-state value option, `--x true|false`) where `CliFlag?` (presence-only) was meant — is
  a candidate future rule, deferred rather than shoehorned onto a non-existent attribute.

  New conventions introduced post-1.0 ship with their analyzer or are explicitly deferred with a
  charter-level rationale.

- **✅ XML `<summary>` + `<example>` on every public method — met, and ENFORCED (POR-27).**
  It was inherited as "✅ audited" and was false: `CliApplication.Create` — the primary entry point —
  had no `<summary>` at all, nor did the four `CliMiddleware` override points or the two concrete
  middlewares'; and the whole `CliPrompt` surface had no `<example>`. All are now documented, and
  `CliOptions.IsAssignableFrom` — a public member nobody meant to ship — is `internal`.

  **The gate no longer depends on anyone re-auditing it.** `Portico_XmlDocGate_Should` reflects over
  the exported surface of the built assembly and cross-references the shipped `Portico.xml` on every
  build (a source-grep is misleading — `CliApplication`'s private nested `Builder` has `public`
  members that are not exported, and a grep reports ~130 phantom gaps). A new public member that
  ships without a `<summary>` and an `<example>` now fails the build. That is the difference between
  a gate and a decoration, and it is why this one decayed in the first place.

  **Charter-level exemptions** (intentionally exampleless): public constructors of the attribute
  types (the type-level example shows the canonical applied form); trivial positional/record
  properties and value-carrier records (`CliInvocation.ExecutableName/Options/Segments`,
  `CliTestRunResult` carriers); compiler-generated record members (`<Clone>$`, `Deconstruct`), which
  cannot carry docs at all; the trivial `CliMiddleware()` ctor and exception ctors; and
  `CliShortOptionSchema` (internal — no public surface).
  CommandLine, verify semantics match any BCL member with the same name, or the name is
  unique enough that a BCL-prior agent won't collide. Any "name matches BCL but flips
  semantics" is a hard blocker.
- **`[CliCommandExample]` is required on every `[CliRoute]`.** This is already enforced by
  `MissingCommandExampleAnalyzer` (POR004) but also re-verified at release-gate time.
- **`CliContractValidator<T>` runs all examples as tests.** No `[CliCommandExample]` ships
  untested. The examples-are-tests feature is the central agent-friendly mechanism and must
  be exercised on every shipped example.
- **No speculative `.With*` config methods — but "no internal caller" is _not_ the test.**
  Portico is a library: its public builder surface exists to be called by *downstream user
  projects*, so zero callers *inside this repository* is the normal, expected state for a
  fluent affordance and is **never on its own grounds for deletion**. A builder method is
  judged by three questions, in order:
  1. **Value** — would a real downstream user reach for it (does it deliver productivity), or
     was it added purely on spec with no user story? Only a *no* here makes it speculative.
  2. **Proof** — is it exercised by at least one `<example>`-as-test that shows it working?
  3. **Non-duplication** — is it free of literal 1:1 behavioural duplication with another
     public method on the same type?

  A method that fails (1) is speculative and gets deleted. A method that passes (1) but fails
  (2) gets an **example-as-test, not deletion**. A method that fails (3) gets **consolidated on
  merit** (the surviving form keeps the example). Deletion is reserved for genuinely
  speculative or superseded surface — it is never triggered by internal caller-count alone.
  Speculative *un-built* proposals remain rejected per `feedback_simplicity_first.md` +
  `feedback_agent_first.md` §7; this bullet governs surface that already ships.

## 7. Positioning sentence

> **"ASP.NET Core for the terminal. Your routes are routes, and your examples are executable tests —
> so the CLI cannot lie about what it accepts."**

Every piece of public copy must pass the "would a .NET developer recognize this in 5 seconds?" test.

### What the pitch is NOT (POR-44)

The load-bearing clause is the second one. The first is recognition; the second is the claim.

- **Not "declarative attributes".** Attribute routing is **table stakes** in 2026 — clap derive (Rust),
  picocli (Java), typer (Python), kong (Go), oclif (Node) all have it. Leading with it concedes the
  argument to everyone.
- **Not "less boilerplate".** Boilerplate cost is approaching zero: an agent will emit two hundred
  lines of builder wiring and never tire. Brevity is not a benefit to a tireless author, so a pitch
  built on it is dead on arrival.
- **Not "declarative is better for LLMs".** That is *unmeasured* (POR-42). It must not appear on any
  public surface until it is measured, however plausible it sounds.

What is scarce is not typing speed. It is **ground truth**: a description of the command surface that
cannot drift from the surface itself. Every incumbent's examples are free text —
`cobra.Command.Example`, oclif's `examples`, yargs' `.example()`, OpenCLI's `examples: [string]` — printed
in help, checked by nobody. Portico's are executed through the real pipeline against the real contract,
and a stale one fails the build.

### The prior art we concede, by name

An honest pitch names the people who got there first. **"Nobody thought to validate examples" is false
and must never be said.**

- **Azure CLI.** Its `azdev linter` has `faulty_help_example_parameters_rule`, which parses each help
  example through the **real** command parser and fails CI on an invalid one. Verified in
  `azdev/operations/linter/rules/help_rules.py`, not assumed. That is genuine, shipped prior art. The
  honest distinction: Microsoft built bespoke tooling for *one* CLI, checking that an example's options
  are *recognised*; Portico makes it the framework's central abstraction, checking that an example
  **dispatches to a specific handler and binds specific values**.
- **trycmd** (Rust) executes README examples as snapshot tests — the closest thing in a mainstream
  ecosystem.
- **docopt** derived the parser from the help text, attacking the same drift from the opposite end.

The claim that survives all three: *no .NET CLI framework makes verified examples the contract*, and no
framework in any ecosystem checks that an example reaches the handler it names with the values it names.
