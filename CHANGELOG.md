# Changelog

All notable changes to Portico are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Portico is **0.x**: SemVer's 0.x licence — *anything may change* — **is** the preview channel.
There are no alpha/beta feeds. Breaking changes land in minor versions and are called out below.

## [Unreleased]

### Added

- **POR012** (Warning) reports a `[CliOption]` on a `bool` — the framework's own most common misuse.
  `CliFlag?` is presence-only (`--dry-run`); a `bool` reads a value (`--force true`), so a bare
  `--force` does not set it. The code fix rewrites the declaration to `CliFlag? … = null`. `bool`
  stays fully supported for a genuine two-state option, which is why this is a Warning — suppress it
  with `#pragma warning disable POR012` when you meant the value.
- **POR013** (Warning) reports a `catch` clause in a command handler that swallows
  `CliExitException`. A catch-all between the throw and the framework's exit boundary silently
  downgrades a controlled exit, so a failed command can return 0 — and for a CI step or a deployment
  gate, which read the exit code and nothing else, that is a green build over a broken migration.
  The code fix adds `when (ex is not CliExitException)`. The rule sees the handler body only; its
  limits are documented in `docs/reference/analyzer-rules.md`.
- A map option accumulates repeated keys when its value type is a collection —
  `Dictionary<string,string[]>` binds `--header[Accept] json html` and
  `--header[Accept] json --header[Accept] html` identically. Headers, labels and selectors repeat
  keys as a matter of course, and `?tag=a&tag=b` is canonical query-string form, which is where map
  options come from. `Dictionary<string,T>` still rejects a repeated key — nothing became last-wins.
  The rule is independent of the container, so every supported map shape accumulates.
- `--help` names the environment variable an option falls back to — `(env: APP_HOST)` — for method
  parameters and `CliOptions` bundle properties alike. **The name, never the value**: reading the
  value is the leak that kept this out of the ecosystem
  ([dotnet/command-line-api#1191](https://github.com/dotnet/command-line-api/issues/1191), open since
  2021), and it stays a leak whether or not the option is marked `Sensitive`. A sensitive option
  shows its variable name and still renders its default as `***`.
- Compact durations bind: `--timeout 90s`, `1h30m`, `500ms`, in any case and with optional
  whitespace. These are the forms operators arrive with from Go durations, `kubectl --timeout`,
  systemd and Prometheus. Milliseconds are new; units are now `ms`, `s`, `m`, `h`, `d` and their
  spelled-out forms.
- Map options bind to `IDictionary<string,V>`, `IReadOnlyDictionary<string,V>`,
  `SortedDictionary<string,V>`, `ImmutableDictionary<string,V>`, `IImmutableDictionary<string,V>`
  and `ImmutableSortedDictionary<string,V>`, not only `Dictionary<string,V>`. The two interfaces are
  the most idiomatic way to declare a map in a signature and were previously rejected outright.

### Fixed

- **A nullable struct collection — `ImmutableArray<T>?` — now binds.** `ImmutableArray<T>` is a
  struct, so the nullable form is the only way to write an *optional* immutable-array option; it was
  refused at `CliApplication.Create` with *"has type 'Nullable\`1', which cannot be built from a
  command-line string"*, telling the user to put a `[TypeConverter]` on a BCL generic they cannot
  modify. `CanAccept` and the POR010 analyzer had unwrapped nullables all along — only the
  materializer's shape detection disagreed. The same unwrap now reaches the map detector, so a
  nullable map is diagnosed as a map instead of an unconvertible scalar.

- **A refused option type is named the way it was written.** The last refusal still spelling raw CLR
  names reported `Nullable\`1` or `Queue\`1`, which names nothing the user wrote and gives them no
  type to act on.

- **`[CliOption(DefaultValue = "…")]` on a collection now binds.** It was converted through the
  *element* converter, so `DefaultValue = "eu,us"` on a `string[]` produced a `string` and failed
  inside `MethodInfo.Invoke` at exit 1; an `int[]` failed earlier with *"1,2 is not a valid value for
  Int32"*, which never said the value was being read as a list. It comma-splits now, matching the
  environment-variable path. On a **map** it is refused at `CliApplication.Create` — one string
  cannot carry key/value pairs — where it was previously accepted and then **silently ignored**.

- **`Portico.DependencyInjection`'s package README sample now compiles** against the dependencies the
  package declares. It called `BuildServiceProvider()`, which lives in
  `Microsoft.Extensions.DependencyInjection` — a package the adapter deliberately does not depend on
  — and showed no `using` directives, so `using Portico.DependencyInjection;` was missing and the
  compiler bound to the core package's `AddCommands<T>(Func<T>)` instead. Both defects shipped in
  0.1.0 and 0.1.1. The samples in every package README are now compiled in CI against each
  package's own dependency closure, one project per package.

- **Breaking:** an absent optional collection option binds an **empty collection**, not `null`, so a
  handler can iterate it without a null check. A map option in the same position already bound an
  empty dictionary, so this removes an inconsistency rather than inventing a convention; and argv has
  no syntax for an explicitly empty list, so "absent" and "supplied with zero values" were never
  distinguishable at the terminal anyway. A collection with no `?` and no default is still
  **required** and still errors when absent. `CliFlag?` is unchanged — absent means "off", and `null`
  is how that is spelled.
- **Breaking:** `--timeout 30` no longer binds thirty **days**. A bare number is a day count to
  .NET's `TimeSpan` parser, so the one value in the duration converter that failed did so silently —
  on a `drain` or a `migrate`, an outage. It is refused now, with a message naming the repairs
  (`30s`, `30 seconds`, `00:00:30`). It is deliberately *not* reinterpreted as seconds: the same
  string would then mean one thing in Portico and another in every other .NET tool.
- A rejected duration says what would have worked. The message was `Invalid timeout format: X`,
  which restated the input and named none of the four accepted forms — and it is the message every
  non-ISO-8601 failure lands on.
- An option whose declared type Portico cannot construct is now refused at `CliApplication.Create`
  with a message naming the option and the shapes that work. Acceptance and construction were
  decided in two places that could disagree: `CanAccept` answers on the *element* type, so
  `Queue<string>`, `Collection<string>`, `SortedDictionary<string,string>` and five other
  collection- or map-shaped types looked bindable, fell through to the scalar materializer, and
  failed inside `MethodInfo.Invoke` at exit 1 with a raw .NET type name — no startup error, no usage
  error, no compile error.

- `dotnet new portico-cli` now references the `Portico` version that its template package shipped
  with. The default was the hardcoded string `0.1.*`, typed into the tracked `template.json` and
  substituted by nothing — so every release after 0.1.x would have scaffolded a 0.1 reference,
  handing the newest user the oldest supported line while the docs they then read described a
  version they were not on. It is derived from the build now, like every other version in the
  repo. Pass `--porticoVersion` to override it, as before.

## [0.1.1] - 2026-07-29

**The three companion packages that 0.1.0 promised but did not ship.** No code changed between
0.1.0 and 0.1.1 — `Portico` 0.1.1 is 0.1.0 rebuilt from the same tree.

`Portico.DependencyInjection`, `Portico.Hosting` and `Portico.Templates` were rejected by
nuget.org at 0.1.0 with *"The package ID is reserved"*: the `Portico.*` prefix was reserved,
while the bare `Portico` was not, so the core package published and its three children did not.
The prefix has since been reserved for this account, and all four IDs now publish together.

`Portico` 0.1.0 remains listed and usable — the framework, the analyzers and `Portico.Testing`
all ship inside it. Take 0.1.1 if you want the DI or Hosting adapters, or the
`dotnet new portico-cli` template.

### Fixed

- The release workflow no longer passes `--skip-duplicate` to `dotnet nuget push`. That flag
  turned three 409 Conflicts into a green 0.1.0 that shipped one package out of four. A conflict
  during a release is never benign: the version is immutable, so a silent skip means the release
  did nothing, and a conflict on a new id means the id is unavailable.
- A new gate polls nuget.org after the push and fails the job unless **every** package resolves
  at the tagged version, before the GitHub Release is created. A 201 from the push endpoint means
  the upload was accepted, not that the package exists.

## [0.1.0] - 2026-07-28

The first public release. Everything below is new to anyone outside the repository — the
`Fixed`, `Changed` and `Removed` subsections record what moved during development, and are kept
because *why* a decision was reversed is usually more useful than the decision itself.

### Highlights

- **Your examples are executable tests.** One `CliContractValidator<T>` test runs every
  `[CliCommandExample]` through the real dispatch pipeline. A stale example fails, so the CLI
  cannot lie about what it accepts.
- **Ten Roslyn analyzers, bundled.** `POR001`–`POR011` (POR007 retired) catch contract mistakes at
  compile time. No second package to install.
- **A zero-dependency core**, asserted against the packed `.nupkg`'s own nuspec, per target
  framework.
- **DI and Generic Host are opt-in**, in separate packages, which is what keeps the core claim
  true rather than merely convenient.
- **`net8.0` and `net10.0`**, both tested in CI.

### Added

- `CliOptionAttribute.Sensitive` — mark an option's value a secret. It is redacted (`***`) wherever
  the framework renders it: help defaults, trace output, timing output, conversion errors, arity
  diagnostics.
- Optional trailing positionals: a C# default on a `[CliArgument]` parameter now makes the
  positional optional, and help renders it as `[NAME]` rather than `<NAME>`.
- `docs/` — the Charter, the extensibility guide, the AOT decision, and the roadmap.
- `examples/AdminCli` — a worked backend admin CLI, contract-tested by CI.
- **`Portico.Templates`** — `dotnet new portico-cli` scaffolds a runnable CLI with one route, one
  executable example, and a **passing contract test**. The point is the loop, not the boilerplate: the
  scaffold builds with zero warnings under the analyzers, and `dotnet test` runs its help examples
  through the real pipeline on the first try. A CI job installs the packed template, scaffolds, builds
  and tests it on every push.
- **Two new analyzers close the last gaps in attribute-contract coverage.** `POR009` — two options on
  one command declaring the same alias (parameters, bundle properties, or one of each), which used to
  fail only at `CliApplication.Create`. `POR010` — a `[CliOption]` whose type cannot be built from a
  command-line string. POR010 is deliberately conservative: it fires only for a type declared in your
  own code, because whether a *referenced* type has a `TypeConverter` is a runtime fact Roslyn cannot
  see, and a false positive at `Error` severity would fail a build that works. The runtime checks
  remain as backstops for builds without the analyzer.
- **`POR011` — a route that declares the same `{placeholder}` twice.** `[CliRoute("copy {path}
  {path}")]` resolves both slots to one parameter, and at dispatch the second value overwrites the
  first. That is silent data loss, and it is the one mistake `CliContractValidator` cannot catch:
  the example still dispatches, so the contract test reports a pass while a value is discarded. A
  false green in the framework's central verification mechanism is worth an `Error`, which is why
  this rule exists even though `CliApplication.Create` already rejects the route at runtime.
- **`Portico.Hosting`** — Generic Host integration. `builder.Services.AddPorticoCommands<IAdminTool,
  AdminTool>()` then `await builder.Build().RunPorticoAsync(args)`, which returns the command's exit
  code for `Main` to return. **Graceful shutdown is one mechanism, not two**: the host's
  `IHostApplicationLifetime` owns Ctrl+C and SIGTERM, and because the token it hands over can be
  cancelled, the core deliberately skips its own SIGINT/SIGTERM wiring instead of racing it. A
  cancelled command still exits 130. The CLI is not modelled as an `IHostedService` — a CLI is one
  command, one invocation, one exit code.
- **`Portico.DependencyInjection`** — the `Microsoft.Extensions.DependencyInjection` adapter.
  `cfg.AddCommands<IAdminTool>(serviceProvider)` resolves your command contract from the container,
  and `cfg.UseMiddleware<AuditMiddleware>(serviceProvider)` does the same for middleware. Each
  dispatched command runs in its own `IServiceScope`, disposed when it completes — success, failure,
  or cancellation — so `AddScoped` behaves the way a backend team expects rather than silently
  resolving from the root. The factory stays lazy: nothing is constructed by `Create`, by `--help`,
  or by an unknown command, and a dispatched route constructs only the command it reached. The core
  `Portico` package still declares **no** dependencies; this one declares
  `Microsoft.Extensions.DependencyInjection.Abstractions` and nothing else.
- **XML docs on the whole public surface, enforced by a test.** `CliApplication.Create`, the
  `CliMiddleware` lifecycle hooks and `Clone`, and the `CliPrompt` / `CliCompletion` / `CliHelpBuilder`
  members now carry a `<summary>` and a usage `<example>`. A new public member that ships without them
  fails the build (`Portico_XmlDocGate_Should`) — the surface an agent reads cannot silently decay.
- `docs/reference/capabilities.md` — the whole shipped surface (env-var fallback, map options,
  `Sensitive`, `CliFlag?` vs `bool`, human-readable durations, route ranking, completion, bundles),
  every entry backed by an executable test in `CliCapabilities_Should`.
- `docs/reference/analyzer-rules.md` — the ten rules, at the anchors every POR00x diagnostic has
  always linked to. The page did not exist, so every analyzer message pointed at a 404.
- `docs/explanation/alternatives.md` — what each competing .NET CLI framework is better at, with
  versions and the date the landscape was checked, and the one claim Portico makes stated precisely
  enough to be falsified.
- `docs/how-to/migrate-from-cocona.md` — a migration guide for Cocona users (Cocona was archived
  2025-12-14), including where ConsoleAppFramework or System.CommandLine is the better destination.
- **A package identity: one icon, and descriptions written to be found.** All four packages carry
  the same embedded 128×128 icon — a portico framing a shell prompt — generated from
  `eng/brand/generate.py`, which is also the source of the SVG master, the 512×512 square and the
  1280×640 social preview. The geometry lives in the script rather than in a binary nobody can
  edit, so the assets are reproducible and a colour change is a one-line diff. Package descriptions
  and tags were rewritten as search copy: the tagline still opens the README, but the fields NuGet
  actually matches a query against now lead with the words someone types. A packaging test asserts
  the icon and README ship in every package, because nuget.org renders a placeholder for a missing
  icon rather than failing.
- `docs/how-to/compose-clis.md` + `examples/PlatformCli` — mounting several independently-built
  contracts into one binary, with the composed surface contract-tested by CI. Both mounted tools
  declare a route called `status`; the mount point disambiguates them.

### Fixed

- **Middleware teardown order no longer depends on the target framework.** `CliMethodInvoker`
  reversed a `CliMiddleware[]` with `bundles.Reverse()`. On `net10.0` that binds to LINQ's
  `Enumerable.Reverse` and is correct — the shipped behaviour was never wrong — but on `net8.0` an
  array binds to `MemoryExtensions.Reverse(Span<T>)`, which reverses **in place** and returns
  `void`. Same source, different meaning per target. Now spelled `Enumerable.Reverse(...)`
  explicitly. Only the chained `.ToArray()` made the compiler catch this instead of the pipeline
  quietly tearing down in registration order on one target and reverse order on the other,
  undoing POR-72.

- **`--opt=value` now binds.** The GNU long-option form that git, docker, curl and dotnet all accept
  — and that users type without thinking — did not work **at all**: `myapp --name=x` exited 2 with
  "unknown option" for an option plainly listed in `--help`. The assignment split lived in the string
  tokenizer, which a real shell's argv never passes through, so it worked in `Run(string)` and failed
  for every real invocation. It now happens where every path meets. Scalars, collections and maps all
  take both forms; everything after the first separator is the value, verbatim (`--filter=name=foo`);
  a quoted value with spaces survives; and after the `--` terminator a glued token is left alone.
- **stderr is no longer a prompt-injection channel.** Everything the framework echoes back — the
  command line you typed, a value that failed to convert — is attacker-influenced input. It now has
  ANSI escapes and invisible codepoints stripped, so a crafted command line cannot rewrite a terminal
  or smuggle text a human reviewer cannot see but a model still reads. **Handler output is untouched:**
  a handler owns its bytes.
- **`EnvironmentVariable` now works on flags and collections, and refuses maps out loud.** It was
  honoured by scalar options and **silently inert** everywhere else — a containerized service set the
  variable, nothing happened, and there was nothing to debug. A flag is now on unless the variable is
  empty, `0`, `false` or `no` (set-but-empty is off: `docker run -e FOO` passes `FOO=`, and silently
  enabling a flag because of that would be indefensible). A collection reads a comma-separated value.
  A **map** now throws `CliConfigurationException` at `CliApplication.Create` — one variable cannot
  carry key/value pairs without an encoding that breaks on the first value containing a separator, and
  a loud refusal beats a quiet no-op.
- **The analyzers now reach you if you install only an adapter package.** NuGet does not flow analyzer
  assets transitively, so `dotnet add package Portico.DependencyInjection` (or `Portico.Hosting`) used
  to give you the framework with every analyzer **silently switched off** — a green build with none of
  the compile-time checks. The adapters now declare their dependency on the core with
  `PrivateAssets="none"`. Verified by consuming the packed `.nupkg`, not by reading the nuspec.
- **A failing example now tells you why it failed.** `CliContractValidator<T>` knew the reason — the
  framework writes it out — and threw it away, so the signature feature produced a red test that said
  only *that* an example broke. `CliContractExample.FailureReason` now carries the framework's own
  diagnostic (`Unrecognized option(s): --bogus`), and `Validate`'s `onNotInvoked` receives it as a
  second argument. **Breaking**: `onNotInvoked` is now `Action<CliCommandExampleAttribute, string>`.
  The validator also no longer writes those diagnostics to the process console as a side effect —
  asking a question should not print.
- **Top-level `--help` now lists your commands.** It used to concatenate the full detail of every
  command — usage line, arguments, every option — into one wall, with no command list and no
  descriptions. It now prints the `Commands:` summary every CLI a user has met (git, docker, dotnet,
  kubectl) prints, with each route's `[Description]`, and points at `app <command> --help` for the
  detail. A **single-command** CLI still shows that command's full help: a menu of one is not a menu.
  Help lines no longer carry trailing whitespace.
- **A composed CLI can now verify its examples.** `CliContractValidator<T>` registered the
  contract's proxy unmounted, so it passed examples that the real, composed application rejects with
  exit 2. It now takes the root routes the contract ships under —
  `new CliContractValidator<IAwsTool>("aws")` — and runs every example against the mounted route.
  A contract registered at the root is unaffected.
- **Help no longer lies in a composed CLI.** A command mounted under a root route
  (`AddCommands(tool, [new CliRouteAttribute("aws")])`) rendered its `[CliCommandExample]` verbatim —
  `master deploy --region eu-west-1` — which exits 2 when pasted, because the real route is
  `master aws deploy …`. Examples now carry the mount prefix. A type-level `[CliRoute]` prefix is
  unaffected: that one is visible to the example's author and is still expected in the example text.
- **A failed conversion now names the option that rejected the value** — `Value 'abc' for option
  '--amount' is invalid.`, not `The value 'abc' is invalid. (Parameter 'value')`. The internal
  parameter name no longer leaks into user-facing output, on any option shape.
- **Errors and help now name the program the user typed.** An apphost-launched app used to render
  its managed assembly — `Unknown command: admin.dll db migrat` — which is not a name anyone can
  copy out of an error and run. The process-derived name now comes from `Environment.ProcessPath`
  with the extension stripped. An explicitly supplied `argv[0]` (`CliInvocation.FromArgs(string[])`,
  `CliTestHarness.Run("app.exe …")`) is still echoed verbatim; a caller's argv is not reinterpreted.
- **Secrets no longer leak to stderr.** A mistyped command used to echo every option *value* —
  including connection strings and tokens — into the "Unknown command" error, on the default path
  with no middleware enabled. The unknown-command diagnostic now prints the executable and route
  segments only: no route matched, so the framework cannot know which values are secret, and it does
  not guess.
- **Options differing only by case no longer bind to each other.** `-v` used to set `-V` as well,
  silently, with exit code 0. Single-char short aliases are now case-sensitive (preserving the
  `curl -v` / `curl -V` idiom); longer forms remain case-insensitive.
- **`TimeSpan?` now accepts human-readable durations** (`"30 seconds"`, `"5 min"`, `"PT30S"`), as
  `TimeSpan` already did.
- **Analyzer POR006 no longer blocks dependency-injected middleware.** Middleware is user-constructed
  and cloned, never `Activator.CreateInstance`d, so a constructor dependency is legitimate. The rule
  still covers `CliOptions` bundles, which genuinely are Activator-constructed.

- **Portico now targets `net8.0` as well as `net10.0`.** All three packages ship `lib/net8.0` and
  `lib/net10.0`. The core `Portico` package declares an empty dependency group on both — the
  zero-dependency guarantee is per-target, not just on the newest. (`Portico.DependencyInjection`
  and `Portico.Hosting` depend on their `Microsoft.Extensions.*` abstractions on both targets, as
  they always have; keeping those out of the core is the point of the split.) CI builds and tests
  both targets, and `Portico.Packaging.Tests` asserts each package's dependency set per TFM.

  This replaces a "net10.0 only until a real user asks" rule that could not work: a team on .NET 8
  never gets far enough to ask, because the framework requirement stops them at the NuGet page. The
  migration needed two source changes and no `#if` — `[GeneratedRegex]` moved from the C# 13
  partial-property form to the method form every other regex in the codebase already used, and one
  `Reverse()` call was spelled `Enumerable.Reverse(...)` (see Fixed).

### Removed

- **`POR007` is retired.** It reported a parameter carrying two `[CliArgument]`s — a mistake that was
  possible only because `CliArgumentAttribute` declared `AllowMultiple = true` and the framework then
  banned what the attribute had just permitted. The attribute now declares `AllowMultiple = false`, so
  the C# compiler rejects it as **CS0579**: no analyzer reference required, no `#pragma` to suppress
  it, nothing to disable. Handing a check to the compiler beats keeping a rule that only existed to
  undo an attribute's own declaration. The ID is not reused — the next free rule is `POR012`.

  The runtime check at `CliApplication.Create` stays. `AllowMultiple` is a compiler concept, not a
  CLR one, so a subclass of `CliArgumentAttribute` — a documented extension point — that redeclares
  `[AttributeUsage(AllowMultiple = true)]` can still reach it.

### Changed

- **`POR004` is now an `Error`, not a `Warning`.** A `[CliRoute]` with no `[CliCommandExample]`
  breaks the build outright. Examples-are-tests is the one invariant Portico asks you to accept, and
  at `Warning` it held only in projects that happen to set `TreatWarningsAsErrors` — so the README's
  claim that the analyzer "fails the build" was false for an ordinary consumer. Enforcing it is the
  honest resolution; softening the claim was the alternative. `MissingCommandExampleCodeFix` already
  ships, so the fix is one keystroke, and a route that genuinely wants no example can suppress the
  rule per-route — a visible decision rather than a rule that quietly does nothing.
- **`CliOptions.IsAssignableFrom` is now `internal`.** It was a one-line wrapper over
  `typeof(CliOptions).IsAssignableFrom(type)` with no callers outside the framework — public surface
  nobody meant to ship.
- The shell-completion script's heredoc marker is `__PORTICO_ROUTES__`.

[Unreleased]: https://github.com/vvka-141/portico/compare/v0.1.1...main
[0.1.1]: https://github.com/vvka-141/portico/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/vvka-141/portico/releases/tag/v0.1.0
