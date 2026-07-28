# AdminCli

A backend admin CLI for a hypothetical service — `migrate`, `seed`, `reindex`, `drain`, `health`.

## What it demonstrates

- Sensitive options (`--connection-string`, redacted in all framework output)
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
```

## Test it

```
dotnet test examples/AdminCli.Tests
```
