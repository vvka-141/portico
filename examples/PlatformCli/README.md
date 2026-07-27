# PlatformCli

A master CLI that mounts two independently-built tools —
[Platform.Queue](../Platform.Queue) and [Platform.Storage](../Platform.Storage) — under distinct
route prefixes.

## What it demonstrates

- Multi-contract composition (`AddCommands` with a mount-prefix `CliRouteAttribute`)
- Route-name collision resolution (both tools declare `status`; the mount prefix disambiguates)
- Contract testing survives composition (`CliContractValidator<IQueueTool>("queue")`)

## Run it

```
dotnet run --project examples/PlatformCli -- storage status --bucket invoices
dotnet run --project examples/PlatformCli -- storage purge archive --older-than "90 days"
dotnet run --project examples/PlatformCli -- queue status --queue orders
dotnet run --project examples/PlatformCli -- queue drain --timeout "30 seconds"
```

## Test it

```
dotnet test examples/PlatformCli.Tests
```
