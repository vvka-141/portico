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
an option and the build goes red.

The scaffolded project references the exact `Portico` version this template package shipped
with — the default is written at pack time, not typed by hand, so `Portico.Templates` 0.2.0
scaffolds `Portico` 0.2.0 and never a stale line. Pass `--porticoVersion` to pick another:

```
dotnet new portico-cli -n MyCli --porticoVersion 0.1.1
```

Pick the target framework with `-f` / `--framework` — `net10.0` (default) or `net8.0`:

```
dotnet new portico-cli -n MyCli -f net8.0
```

The template requires an 8.0.100 SDK or later and hides itself on anything older, rather than
scaffolding a project you cannot build. Note that `-f net10.0` still needs the .NET 10 SDK: a
`dotnet new` constraint is evaluated before your choices are known, so it can only enforce the floor.

**Full documentation:** [github.com/vvka-141/portico](https://github.com/vvka-141/portico) · **Issues and feedback:** [github.com/vvka-141/portico/issues](https://github.com/vvka-141/portico/issues)

Portico is 0.x. The API is still being shaped by what breaks, so a rough edge is worth an
issue rather than a workaround.
