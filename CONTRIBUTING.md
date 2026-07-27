# Contributing to Portico

Portico is early and has one maintainer. That shapes everything below: the process is short because a
long one would be theatre.

## Build and test

```bash
dotnet restore portico.sln
dotnet build   portico.sln -c Release
dotnet test    portico.sln -c Release
```

Two things will fail your build that may surprise you:

- **`TreatWarningsAsErrors` is on.** A warning is a build failure. This includes XML-doc warnings.
- **The public surface is gated by tests.** A new public member without an XML `<summary>` and an
  `<example>` fails `Portico_XmlDocGate_Should`. The public API is what agents and humans read; it is
  not allowed to decay quietly.

Target frameworks are `net8.0` and `net10.0`. Dependencies go in `Directory.Packages.props` (central
package management), never in an individual `.csproj`.

## The rules that are not negotiable

These are the framework's constitution, not preferences. If a change needs one of them relaxed, say
so explicitly in the PR and expect a conversation.

- **The core `Portico` package has zero dependencies.** Anything that needs
  `Microsoft.Extensions.*` belongs in `Portico.DependencyInjection` or `Portico.Hosting` — or it does
  not belong. A test asserts the packed `.nupkg` declares no dependencies.
- **Every change carries a test.** Not "most". A bug fix without a test that fails before it is not a
  fix, it is a coincidence.
- **Examples are tests.** `[CliCommandExample]` is executable. If you change routing or binding,
  expect the examples in `examples/` to tell you about it.
- **No deprecation shims.** There is no user base to keep compatible with yet. Remove things outright
  rather than leaving a corpse behind a `[Obsolete]`.

The reasoning behind the design lives in [`docs/explanation/charter.md`](docs/explanation/charter.md)
— read it before proposing something structural. [`docs/ROADMAP.md`](docs/ROADMAP.md) lists what is
deliberately *not* being built, and why; a PR implementing something on the parked list will be
declined on those grounds, however good the code is.

## Workflow

Trunk-based. `main` is always releasable.

1. Branch from `main`.
2. Make the change, with tests. Keep the commit history readable — one logical change per commit.
3. Open a PR. Say what changed and why; if you changed behaviour, show the before and after.
4. CI must be green: build, test, pack.

Releases are tag-driven and are the maintainer's call. Do not push a `v*` tag.

## Reporting things

- **A security problem** → [SECURITY.md](SECURITY.md). Not a public issue.
- **A bug** → an issue, with the smallest command line that reproduces it and the exit code you got.
- **A feature** → an issue first, before the code. Portico is deliberately narrow (see the Charter),
  and it is a poor use of your evening to write something that will be declined on scope.

## Stability

Portico is **0.x**, and SemVer's 0.x licence — *anything may change* — **is** the preview channel.
There are no alpha or beta feeds. Breaking changes land in minor versions and are called out in
[CHANGELOG.md](CHANGELOG.md). **1.0 is cut when the API is one we would defend**, not when the code is
done.
