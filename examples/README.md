# Examples

Three worked CLIs, each demonstrating a different slice of Portico. All are built and
contract-tested by CI.

| Example | What it demonstrates | Run it |
|---------|---------------------|--------|
| [AdminCli](AdminCli) | A backend admin CLI — sensitive options, timing middleware, map options, `CliFlag`, `TimeSpan`, `CancellationToken`, optional positional arguments | `dotnet run --project AdminCli -- db migrate -c "Host=db"` |
| [ReferenceCli](ReferenceCli) | **The full-surface reference** — everything AdminCli has, plus option bundles with validation, collection options, `RankByOptions` disambiguation, environment-variable fallback, middleware with DI, composition via mount prefix | `dotnet run --project ReferenceCli -- worker list pool-1` |
| [PlatformCli](PlatformCli) | Multi-team composition — two independently-built tools ([Platform.Queue](Platform.Queue), [Platform.Storage](Platform.Storage)) mounted under distinct route prefixes | `dotnet run --project PlatformCli -- storage status --bucket invoices` |

`ReferenceCli` is the ground truth for correct Portico code. If you want to see how a feature
works end-to-end, start there.

## Running the tests

Each example has a `.Tests` project:

```
dotnet test examples/AdminCli.Tests
dotnet test examples/ReferenceCli.Tests
dotnet test examples/PlatformCli.Tests
```

The contract tests verify that every `[CliCommandExample]` still dispatches and binds correctly.
