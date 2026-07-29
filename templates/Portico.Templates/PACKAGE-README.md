# Portico.Templates

Project template for [Portico](https://www.nuget.org/packages/Portico) — a runnable CLI with
one route, one executable example, and a passing contract test.

```
dotnet new install Portico.Templates
dotnet new portico-cli -n MyCli
cd MyCli
dotnet test
```

The template gives you a solution with two projects: a CLI (`MyCli/`) and a test project
(`MyCli.Tests/`). The test runs every `[CliCommandExample]` through the real pipeline — rename
an option and the build goes red.

The scaffolded project references the exact `Portico` version this template package shipped
with — the default is written at pack time, not typed by hand, so `Portico.Templates` 0.2.0
scaffolds `Portico` 0.2.0 and never a stale line. Pass `--porticoVersion` to pick another:

```
dotnet new portico-cli -n MyCli --porticoVersion 0.1.1
```

**Full documentation:** [github.com/vvka-141/portico](https://github.com/vvka-141/portico) · **Issues and feedback:** [github.com/vvka-141/portico/issues](https://github.com/vvka-141/portico/issues)

Portico is 0.x. The API is still being shaped by what breaks, so a rough edge is worth an
issue rather than a workaround.
