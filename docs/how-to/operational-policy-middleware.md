# Build operational policy middleware

A `CliMiddleware` is both a bundle of application-wide options and the behavior controlled by those
options. Use it for policy that must follow every command in an operational binary: dry-run rules,
change-ticket enforcement, approvals, auditing, tracing or shared output modes.

## Define one policy module

```csharp
using Portico;

public interface IDeploymentPolicy
{
    bool IsProduction(CliInvocation invocation);
    bool IsApproved(string changeTicket);
}

public interface IAuditSink
{
    void Record(string message);
}

public sealed class DeploymentPolicyMiddleware(
    IDeploymentPolicy policy,
    IAuditSink audit) : CliMiddleware
{
    [CliOption("--dry-run", "Plan the operation without changing anything")]
    public CliFlag? DryRun { get; set; }

    [CliOption("--change-ticket", "Approved change ticket")]
    public string? ChangeTicket { get; set; }

    [CliOption("--audit", "Record the invocation in the audit sink")]
    public CliFlag? Audit { get; set; }

    public override void OnExecutingAction(CliInvocation invocation)
    {
        if (policy.IsProduction(invocation) &&
            (string.IsNullOrWhiteSpace(ChangeTicket) || !policy.IsApproved(ChangeTicket)))
        {
            throw new CliExitException(
                "A valid --change-ticket is required for production operations.")
            {
                ExitCode = CliExitException.UsageErrorExitCode,
            };
        }

        if (Audit is not null)
        {
            audit.Record($"start: {invocation}");
        }

        if (DryRun is not null)
        {
            Console.Error.WriteLine("dry-run policy active");
        }
    }

    public override void OnActionExecuted(CliInvocation invocation)
    {
        if (Audit is not null)
        {
            audit.Record($"done: {invocation.ExecutableName}");
        }
    }

    public override void OnError(CliInvocation invocation, Exception exception)
    {
        if (Audit is not null)
        {
            audit.Record($"failed: {invocation.ExecutableName}: {exception.GetType().Name}");
        }
    }
}
```

Every route now recognizes the three options. The policy and its controls cannot drift into separate
registration files because they are one object.

`CliInvocation.ToString()` redacts values of options marked `Sensitive`, so it is suitable for the
audit record. Handler output remains the handler's responsibility.

## Register it directly

Construct middleware with its dependencies and register it once:

```csharp
var deploymentPolicy = new DeploymentPolicy(...);
var audit = new AuditSink(...);

return CliApplication.Create(cfg => cfg
    .UseMiddleware(new DeploymentPolicyMiddleware(deploymentPolicy, audit))
    .AddCommands(new DeploymentOperations(...)))
    .Run(args);
```

Middleware wraps every registered command. Registration order is nesting order: execution hooks run
forward, while error and completion hooks unwind in reverse.

## Resolve it from dependency injection

With `Portico.DependencyInjection`, register middleware as an application-scoped service:

```csharp
var services = new ServiceCollection()
    .AddScoped<IDeploymentOperations, DeploymentOperations>()
    .AddSingleton<IDeploymentPolicy, DeploymentPolicy>()
    .AddSingleton<IAuditSink, AuditSink>()
    .AddSingleton<DeploymentPolicyMiddleware>()
    .BuildServiceProvider();

return CliApplication.Create(cfg => cfg
    .AddCommands<IDeploymentOperations>(services)
    .UseMiddleware<DeploymentPolicyMiddleware>(services))
    .Run(args);
```

With `Portico.Hosting`, the same middleware can be applied while the host assembles its registered
contracts:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddPorticoCommands<IDeploymentOperations, DeploymentOperations>();
builder.Services.AddSingleton<IDeploymentPolicy, DeploymentPolicy>();
builder.Services.AddSingleton<IAuditSink, AuditSink>();
builder.Services.AddSingleton<DeploymentPolicyMiddleware>();

await using var host = builder.Build();
return await host.RunPorticoAsync(
    args,
    cfg => cfg.UseMiddleware<DeploymentPolicyMiddleware>(host.Services));
```

Command handlers resolved through the DI adapter receive a fresh scope per dispatch. Middleware is
different: it is resolved once and shallow-cloned for each invocation.

The public middleware contract does not expose the application's internal `ICliConsole`. Consumer
middleware writes through `Console`; `CliTestHarness` captures those streams for tests.

## Understand the clone boundary

Portico binds global option values onto a per-dispatch clone. Scalar properties such as `DryRun` and
`ChangeTicket` therefore belong to that invocation.

The clone is made with `MemberwiseClone`. Constructor-injected reference fields are shared across
invocations. Inject stateless or thread-safe services; do not inject a mutable per-invocation list and
expect Portico to duplicate it.

Use lifecycle fields only when the hooks themselves assign and clear them for the current dispatch.
Prefer local variables and service methods when possible.

## Validation belongs in the policy hook

`CliOptions` command bundles use DataAnnotations and `IValidatableObject`. Middleware option
properties are materialized, but the middleware object is not automatically passed through bundle
validation. Validate cross-option policy in `OnExecutingAction` and throw `CliExitException` with a
deliberate exit code when the handler must not run.

This is appropriate for rules such as:

- production requires an approved change ticket;
- `--force` and `--dry-run` cannot appear together;
- auditing is mandatory for mutating commands;
- a selected environment determines which credentials must be present.

Keep business validation in the application service. Middleware should enforce invocation-wide
operational policy, not become a second domain layer.

## Test the policy and the complete application

Unit-test the hook for its decision matrix, then use `CliTestHarness` to prove the option is global
and that a rejected invocation never reaches its handler:

```csharp
var harness = CliTestHarness.ForApplication(cfg => cfg
    .UseMiddleware(new DeploymentPolicyMiddleware(policy, audit))
    .AddCommands(operations));

harness.Run("ops deploy billing --change-ticket CHG-142")
    .ExpectExit(0);

harness.Run("ops deploy billing")
    .ExpectExit(CliExitException.UsageErrorExitCode)
    .ExpectError("--change-ticket");
```

Remember that `CliTestHarness` redirects the process-global console while it runs; unrelated tests
that touch `Console` should be placed in a non-parallel test collection.

## Next

- [Define domain-specific options](domain-specific-options.md)
- [Package command capabilities as assemblies](package-command-capabilities.md)
- [Middleware reference](../explanation/extensibility.md#4-cross-cutting-behavior--climiddleware)
