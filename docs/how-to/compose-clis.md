# Compose several CLIs into one binary

A platform team ends up with one operator-facing binary and several teams behind it. Portico lets
each team ship its own contract — its own routes, its own examples, its own test suite — and lets the
platform team decide where each one hangs in the command tree.

```csharp
CliApplication
    .Create(cfg => cfg
        .AddCommands(new StorageTool(), [new CliRouteAttribute("storage")])
        .AddCommands(new QueueTool(),   [new CliRouteAttribute("queue")])
        .WithVersion("platform 1.0.0"))
    .Run(args);
```

```
platform storage status --bucket invoices
platform queue   status --queue orders
```

Worked, CI-built example: [`examples/PlatformCli`](../../examples/PlatformCli) — a master CLI over two
independently-built tools, with the composed surface contract-tested in
[`examples/PlatformCli.Tests`](../../examples/PlatformCli.Tests).

## The mount point is what disambiguates, not the route name

Both tools above declare a route literally called `status`. Neither team knew about the other, and
neither had to. The root route supplied at `AddCommands` is prepended to every route the contract
declares, so the two land at `storage status` and `queue status` and never collide.

**A mount is a move, not an alias.** Once `StorageTool` is mounted under `storage`, the bare `status`
route does not exist:

```
$ platform status
Unknown command: platform status. Run with --help to list available commands.   (exit 2)
```

That is deliberate. If the unmounted form survived, mounting a second contract with the same route
would reintroduce exactly the ambiguity the mount exists to remove.

Prefixes compose. A type-level `[CliRoute("db")]` on the contract stacks underneath the mount:
mounted under `ops`, its `migrate` method answers to `ops db migrate`.

## Verify the surface you actually ship

Composition without verification is the less valuable half, and it is the half that is easy to get.
Tell the validator where the contract is mounted, and it runs every `[CliCommandExample]` against the
route the operator will actually type:

```csharp
new CliContractValidator<IStorageTool>("storage").Enumerate();
```

Validate the contract unmounted and you are verifying a CLI nobody runs: the examples pass there and
exit 2 in the binary you shipped.

To verify against the *whole* composed route table — colliding literals and all — compose the other
tools in through `configureApplication`. The contract under test is proxied; the rest are real:

```csharp
new CliContractValidator<IStorageTool>("storage").Enumerate(
    configureApplication: cfg => cfg.AddCommands(new QueueTool(), [new CliRouteAttribute("queue")]));
```

**Write examples against the contract, never against the mount.** `[CliCommandExample("status")]`,
not `[CliCommandExample("storage status")]`. The mount is the composer's choice; the framework
prepends it — in help, and in the validator. Spell it out yourself and you get `storage storage
status`, which routes nowhere. (The validator catches this, which is the point of it.)

## What this is not

- **Composition is not novel.** oclif's plugin architecture (Salesforce CLI, Heroku CLI) is a mature
  version of this, and cobra can graft any subtree. What is unusual is that here every example in the
  composed surface is still executable and still verified — the mount does not create a blind spot.
- **Sub-CLIs are .NET assemblies you reference.** You cannot mount a Go binary, and this is not a
  wrapper over the real `aws` or `az`. "One master CLI over storage and queues" works because both
  tools are yours.
- **There is no plugin discovery, no isolation, no independent versioning.** Composition is
  compile-time: you reference the assembly and you mount it. Loading commands from external
  assemblies at runtime is on the [parked list](../ROADMAP.md) — it is a security and boundary
  problem, not a missing feature.

## Shipping the pieces separately

Each tool can be its own NuGet package: contract, implementation, and its own test project that
validates the contract *unmounted* (that is the surface that package promises). The master CLI
references the packages, mounts them, and runs the composed validation shown above. The example in
this repository is laid out that way — `Platform.Storage` and `Platform.Queue` are separate
assemblies, and `PlatformCli` is the only thing that knows both exist.

Keeping two contracts in the **same** assembly is fine too. `POR002` (duplicate route) is scoped to
the declaring type precisely because of this: two contracts that each declare `status` are a legal
program once they are mounted apart, and the analyzer says nothing about it. Separate assemblies are
a packaging and ownership choice, not a workaround.
