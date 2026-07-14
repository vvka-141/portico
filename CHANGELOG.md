# Changelog

All notable changes to Portico are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Portico is **0.x**: SemVer's 0.x licence — *anything may change* — **is** the preview channel.
There are no alpha/beta feeds. Breaking changes land in minor versions and are called out below.

## [Unreleased]

### Added

- `CliOptionAttribute.Sensitive` — mark an option's value a secret. It is redacted (`***`) wherever
  the framework echoes the command line: trace output, timing output, conversion errors.
- Optional trailing positionals: a C# default on a `[CliArgument]` parameter now makes the
  positional optional, and help renders it as `[NAME]` rather than `<NAME>`.
- `docs/` — the Charter, the extensibility guide, the AOT decision, and the roadmap.
- `examples/AdminCli` — a worked backend admin CLI, contract-tested by CI.
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
- `docs/how-to/migrate-from-cocona.md` — a migration guide for Cocona users (Cocona was archived
  2025-12-14), including where ConsoleAppFramework or System.CommandLine is the better destination.
- `docs/how-to/compose-clis.md` + `examples/PlatformCli` — mounting several independently-built
  contracts into one binary, with the composed surface contract-tested by CI. Both mounted tools
  declare a route called `status`; the mount point disambiguates them.

### Fixed

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

### Changed

- **`CliOptions.IsAssignableFrom` is now `internal`.** It was a one-line wrapper over
  `typeof(CliOptions).IsAssignableFrom(type)` with no callers outside the framework — public surface
  nobody meant to ship.
- The shell-completion script's heredoc marker is `__PORTICO_ROUTES__`.

[Unreleased]: https://github.com/vvka-141/portico/commits/main
