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

### Fixed

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

- The shell-completion script's heredoc marker is `__PORTICO_ROUTES__`.

[Unreleased]: https://github.com/vvka-141/portico/commits/main
