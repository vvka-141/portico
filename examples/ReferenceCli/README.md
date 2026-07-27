# ReferenceCli

The full-surface, contract-tested reference CLI. If you want to see how a Portico feature works
end-to-end, this is the place to look.

## What it demonstrates

Everything AdminCli has, plus:

- Option bundles with `IValidatableObject` (`JobSpec`)
- Collection options (`List<string>`)
- `bool` options (two-state `--follow true/false`, vs `CliFlag?` which is presence-only)
- `RankByOptions` disambiguation (`run {command}` alongside `run migrate`)
- Environment-variable fallback (`EnvironmentVariable = "FLEET_TOKEN"`)
- `DefaultValue` on attribute
- Composition via mount prefix (`IDiagnosticsTool` mounted under `diag`)
- Middleware with DI (`AuditMiddleware` with a global `--audit` flag)
- Factory-based `AddCommands(() => new FleetTool(clock))`

## Run it

```
dotnet run --project examples/ReferenceCli -- worker list pool-1
dotnet run --project examples/ReferenceCli -- job submit --spec deploy.yaml --tag v2 --tag latest
dotnet run --project examples/ReferenceCli -- cluster ping --token secret
dotnet run --project examples/ReferenceCli -- diag health
```

## Test it

```
dotnet test examples/ReferenceCli.Tests
```
