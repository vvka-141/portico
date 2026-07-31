# Portico.Templates

Templates for [Portico](https://www.nuget.org/packages/Portico), covering both onboarding paths.

**`portico-cli`** — a whole new CLI: one route, one executable example, and a passing contract test.

**`portico-command`** — one command added to a project you already have: the contract interface, its
implementation, and an example that already dispatches. This is the path for a backend service that
runs `dotnet add package Portico` and needs somewhere to go.

```
dotnet new install Portico.Templates
dotnet new portico-cli -n MyCli
cd MyCli
dotnet test
```

Into an existing project, from that project's directory:

```
dotnet new portico-command -n Migrate --route "db migrate"
dotnet new portico-command -n Backfill --async     # Task<int> + CancellationToken
```

The namespace comes from the project's `RootNamespace`, and the emitted code builds clean under
`TreatWarningsAsErrors` — no `POR004`, `POR008`, `POR010`, `POR012` or `POR013`.

The template gives you a solution with two projects: a CLI (`MyCli/`) and a test project
(`MyCli.Tests/`). The test runs every `[CliCommandExample]` through the real pipeline — rename
an option and the test suite goes red.

The scaffolded project references the exact `Portico` version this template package shipped
with — the default is written at pack time rather than copied into this README, so matching
`Portico.Templates` and `Portico` versions stay together. Pass `--porticoVersion` only when you
deliberately need another version:

```
dotnet new portico-cli -n MyCli --porticoVersion <version>
```

The default target is `net10.0` — the active LTS, supported to November 2028. Pick `net8.0`
explicitly with `-f` / `--framework` if you are still on that runtime; note it is in maintenance and
its support ends 2026-11-10:

```
dotnet new portico-cli -n MyCli -f net8.0
```

The template requires an 8.0.100 SDK or later and hides itself on anything older, rather than
scaffolding a default project you cannot build. `-f net10.0` still needs the .NET 10 SDK: a
`dotnet new` constraint is evaluated before your framework choice is known, so it can enforce only
the minimum.

**Full documentation:** [github.com/vvka-141/portico](https://github.com/vvka-141/portico) · **Issues and feedback:** [github.com/vvka-141/portico/issues](https://github.com/vvka-141/portico/issues)

Portico is 0.x. The API is still being shaped by what breaks, so a rough edge is worth an
issue rather than a workaround.
