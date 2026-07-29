# AdminCli

A backend admin CLI for a hypothetical service — `migrate`, `seed`, `backfill`, `reindex`, `drain`,
`health`.

`db backfill` is the route [Your first operational command](../../docs/how-to/operational-command.md)
walks through end to end. That page quotes this project rather than hand-writing snippets, so its
code blocks cannot drift from what CI builds.

## What it demonstrates

- Sensitive options (`--connection-string`, redacted in all framework output)
- Environment-variable fallback (`PGCONNSTR` fills `--connection-string`; `--help` names it, never reads it)
- Collection options (`--ids 41 42 43`, or `--ids` repeated; absent binds empty, not null)
- Named POSIX exit codes (`CliExitException.UsageErrorExitCode` when configuration is missing)
- `CliFlag?` (presence-only `--dry-run`, not `--dry-run true`)
- `TimeSpan` parsing (`--timeout "30 seconds"`)
- Map options (`'--shard[name]' count` → `Dictionary<string, int>`; quote the brackets in zsh)
- Optional positional arguments with defaults (`reindex {index}`)
- `CancellationToken` (Ctrl+C propagation)
- `CliTimingMiddleware` (built-in timing output)

## Run it

```
dotnet run --project examples/AdminCli -- db migrate -c "Host=db"
dotnet run --project examples/AdminCli -- db seed --rows 500
dotnet run --project examples/AdminCli -- reindex users '--shard[us-east]' 3
dotnet run --project examples/AdminCli -- health
PGCONNSTR="Host=db" dotnet run --project examples/AdminCli -- db backfill --ids 41 42 43 --dry-run
```

## Test it

```
dotnet test examples/AdminCli.Tests
```
