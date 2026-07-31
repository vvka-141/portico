# Package command capabilities as assemblies

Portico composes .NET contracts from compile-time assembly references when the application starts.
Use that model when several teams own operational capabilities but a platform team ships one
operator-facing binary.

This is not runtime plugin discovery. The composer chooses exact package versions, the C# compiler
checks their service contracts, and `CliApplication.Create` validates the final route table.

## Separate ownership from composition

A useful solution shape is:

```text
Platform.Operations.Language/       shared attributes, value types and policy middleware
Storage.Operations.Contracts/       IStorageOperations
Storage.Operations/                 StorageOperations and its application dependencies
Queue.Operations.Contracts/         IQueueOperations
Queue.Operations/                   QueueOperations and its application dependencies
PlatformCli/                        composition root and process entry point
PlatformCli.Tests/                  assembled-surface tests
```

The split is an ownership choice, not a Portico requirement. Several contracts can live in one
assembly when that matches the system.

## Publish a team-owned contract

The storage team owns a contract written without knowledge of its eventual mount point:

```csharp
public interface IStorageOperations
{
    [CliRoute("status")]
    [CliCommandExample("status --bucket invoices")]
    int Status([CliOption("--bucket")] string bucket);

    [CliRoute("purge {bucket}")]
    [CliCommandExample("purge archive --older-than \"90 days\"")]
    Task<int> PurgeAsync(
        string bucket,
        [CliOption("--older-than")] TimeSpan olderThan,
        CancellationToken cancellation = default);
}
```

Its implementation can reference the same application-layer storage service used by another host:

```csharp
public sealed class StorageOperations(IStorageService storage) : IStorageOperations
{
    public int Status(string bucket) => storage.WriteStatus(bucket);

    public Task<int> PurgeAsync(
        string bucket,
        TimeSpan olderThan,
        CancellationToken cancellation = default) =>
        storage.PurgeAsync(bucket, olderThan, cancellation);
}
```

The command contract depends on Portico and the shared operational vocabulary. The implementation
depends on the application layer it operates. Keep cloud SDK and database details behind that
application service rather than placing them in the contract package.

## Verify the package-owned surface

The storage package tests the contract without a mount prefix because that is the portable surface
the package promises:

```csharp
public static IEnumerable<object[]> Examples() =>
    new CliContractValidator<IStorageOperations>()
        .Enumerate()
        .Select(example => new object[] { example });

[Theory]
[MemberData(nameof(Examples))]
public void Dispatch_Every_Storage_Example(CliContractExample example) =>
    Assert.True(example.Matched, example.FailureReason);
```

This test says nothing about where a platform composer will mount the contract.

## Mount contracts in the final binary

The platform application chooses the public command tree:

```csharp
var services = new ServiceCollection()
    .AddScoped<IStorageOperations, StorageOperations>()
    .AddScoped<IQueueOperations, QueueOperations>()
    .AddSingleton<DeploymentPolicyMiddleware>()
    .BuildServiceProvider();

return CliApplication.Create(cfg => cfg
    .AddCommands<IStorageOperations>(
        services,
        [new CliRouteAttribute("storage")])
    .AddCommands<IQueueOperations>(
        services,
        [new CliRouteAttribute("queue")])
    .UseMiddleware<DeploymentPolicyMiddleware>(services))
    .Run(args);
```

The storage contract's `status` route becomes `storage status`; the queue contract may also declare
`status` because it becomes `queue status`. A mount is a move, not an alias: the bare routes are not
present in the composed binary.

With the Generic Host, mount points are registered alongside the service contract:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddPorticoCommands<IStorageOperations, StorageOperations>("storage");
builder.Services.AddPorticoCommands<IQueueOperations, QueueOperations>("queue");

return await builder.Build().RunPorticoAsync(args);
```

## Verify the surface operators receive

The platform test supplies the mount prefix:

```csharp
var storage = new CliContractValidator<IStorageOperations>("storage");
var queue = new CliContractValidator<IQueueOperations>("queue");

Assert.All(storage.Enumerate(), example => Assert.True(example.Matched));
Assert.All(queue.Enumerate(), example => Assert.True(example.Matched));
```

To include neighboring contracts in the route table while validating one surface:

```csharp
new CliContractValidator<IStorageOperations>("storage").Enumerate(
    configureApplication: cfg => cfg.AddCommands(
        new QueueOperations(...),
        [new CliRouteAttribute("queue")]));
```

This catches collisions and application-level behavior that a package cannot see in isolation.

Write examples against the unmounted contract (`"status --bucket invoices"`), not against
`"storage status ..."`. The composer and validator prepend the mount. Hard-coding it in the
contract would produce `storage storage status`.

## Treat package versions as the operational bill of materials

The final binary's project references define which command capabilities ship together. That gives
the compiler a complete view, but it also creates release coordination:

- a breaking contract change requires the implementation and composer to update together;
- a shared vocabulary change may affect several team packages;
- the binary should report its own version through `WithVersion`;
- release notes should name capability-package upgrades that alter the command surface;
- runtime plugin loading is intentionally absent, so adding a capability requires rebuilding the
  composer.

This is the trade Portico chooses: an explicit, compiled bill of materials instead of late-bound
plugins whose compatibility is discovered when the process starts.

## Preserve service boundaries

Assembly reuse is strongest when command handlers call an application layer designed for reuse.
Avoid turning the operational binary into an unrestricted collection of internal implementation
types.

- Keep authorization decisions explicit.
- Reuse remote clients rather than bypassing the owning service's API.
- Give the binary only the credentials required by its mounted capabilities.
- Split binaries when one command's blast radius would grant excessive privilege to every operator.
- Expect a large assembly graph to increase startup and deployment size; Portico deliberately trades
  those costs for managed-runtime integration.

## Worked repository example

[`examples/PlatformCli`](../../examples/PlatformCli) mounts independently-built
[`Platform.Storage`](../../examples/Platform.Storage) and
[`Platform.Queue`](../../examples/Platform.Queue) contracts beneath separate prefixes. Its tests
validate the composed routes on both supported target frameworks.

## Next

- [Compose several CLIs into one binary](compose-clis.md)
- [Define domain-specific options](domain-specific-options.md)
- [Build operational policy middleware](operational-policy-middleware.md)
- [Why Portico?](../explanation/why-portico.md)
