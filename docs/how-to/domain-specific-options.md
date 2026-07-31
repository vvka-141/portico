# Define domain-specific options

Use a derived `CliOptionAttribute` when several commands share not only an option spelling, but the
meaning and binding policy behind it. The result is a reusable operational vocabulary:

```csharp
Task<int> DeployAsync(
    [TargetEnvironment] DeploymentEnvironment environment,
    [ChangeTicket] ChangeTicket change,
    [ProductionApproval] ApprovalToken approval,
    CancellationToken cancellation = default);
```

The command reads in domain language. Aliases, help text, environment fallback, sensitivity and
conversion live with that vocabulary rather than being copied across every handler.

## Define the domain value

Keep a useful domain type independent from Portico:

```csharp
public sealed record DeploymentEnvironment(string Name)
{
    public static readonly DeploymentEnvironment Development = new("development");
    public static readonly DeploymentEnvironment Staging = new("staging");
    public static readonly DeploymentEnvironment Production = new("production");
}
```

This type can be used by an API, worker or deployment planner without depending on its command-line
spelling.

## Define the option attribute

The boundary assembly owns the CLI spelling and conversion:

```csharp
using System.ComponentModel;
using System.Globalization;
using Portico;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class TargetEnvironmentAttribute : CliOptionAttribute
{
    public TargetEnvironmentAttribute()
        : base("--environment|-e", "Deployment environment")
    {
        EnvironmentVariable = "DEPLOY_ENVIRONMENT";
    }

    public override bool CanAccept(Type optionType, out TypeConverter converter)
    {
        if (optionType == typeof(DeploymentEnvironment))
        {
            converter = new DeploymentEnvironmentConverter();
            return true;
        }

        return base.CanAccept(optionType, out converter);
    }
}

public sealed class DeploymentEnvironmentConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value) => value is string text
            ? text.ToLowerInvariant() switch
            {
                "development" or "dev" => DeploymentEnvironment.Development,
                "staging" or "stage" => DeploymentEnvironment.Staging,
                "production" or "prod" => DeploymentEnvironment.Production,
                _ => throw new FormatException(
                    $"Unknown deployment environment '{text}'. Use development, staging or production."),
            }
            : base.ConvertFrom(context, culture, value);
}
```

`CanAccept` is the contract between the attribute and Portico's materializer. Return `true` only for
types the converter can actually build from a command-line string. Delegate everything else to the
base implementation so primitives, durations, collections and other built-in shapes retain their
normal behavior.

## Carry policy in the declaration

An organization-owned attribute can set the same metadata as `[CliOption]`:

```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class ProductionApprovalAttribute : CliOptionAttribute
{
    public ProductionApprovalAttribute()
        : base("--approval-token", "Approval token for a production change")
    {
        EnvironmentVariable = "PRODUCTION_APPROVAL_TOKEN";
        Sensitive = true;
    }
}
```

Every command using `[ProductionApproval]` now gets the same spelling, help text, environment
fallback and redaction. The attribute defines boundary policy; the handler or middleware still
decides whether approval is required for a particular operation.

Other useful overrides include:

- `AllowsCsv = false` when values may legitimately contain commas;
- `GetValueComparer()` when string-keyed map options need domain-specific key equality;
- `CanAccept(...)` for a custom value or collection element type.

Derive `CliArgumentAttribute` in the same way when a positional route value needs custom conversion.

## Use the vocabulary from a shared assembly

A practical package layout is:

```text
Platform.Operations.Language/
  DeploymentEnvironment.cs
  TargetEnvironmentAttribute.cs
  ChangeTicket.cs
  ChangeTicketAttribute.cs
  ProductionApprovalAttribute.cs
```

Command-contract packages reference this assembly. The final operational binary then compiles one
consistent vocabulary across every team-owned surface.

## Test the attribute as infrastructure

Test the accepted types and conversion independently of any handler:

```csharp
[Fact]
public void Bind_The_Production_Spelling()
{
    var attribute = new TargetEnvironmentAttribute();

    Assert.True(attribute.CanAccept(typeof(DeploymentEnvironment), out var converter));
    Assert.Equal(
        DeploymentEnvironment.Production,
        converter.ConvertFromInvariantString("prod"));
}
```

Then keep `[CliCommandExample]` on the command contract and run `CliContractValidator<T>` so the
derived attribute is exercised inside the complete route and binding pipeline.

## Current analyzer boundary

The runtime discovers derived attributes by assignability and validates them when
`CliApplication.Create` builds the route table. POR005 also recognizes attributes derived from
`CliArgumentAttribute`. The current option analyzers, however, identify the built-in
`CliOptionAttribute` directly. POR003, POR009, POR010 and POR012 therefore do **not** inspect a
derived option attribute.

Until that gap is closed:

- give the derived attribute focused unit tests;
- exercise every use through contract validation;
- expect a malformed custom declaration to fail application creation rather than compilation;
- prefer a `[TypeConverter]` on the domain type with a base `[CliOption]` when analyzer coverage is
  more important than domain-specific attribute syntax.

This limitation is about edit-loop feedback for derived options, not runtime support. It is
documented here because a domain vocabulary that silently receives weaker compiler assistance would
otherwise overstate the proposition.

## Keep the vocabulary legible

A semantic attribute should make a contract clearer, not conceal arbitrary behavior.

- Use a name an operator and reviewer recognize.
- Keep aliases and descriptions stable across commands.
- Put business execution in application services, not in the attribute.
- Document environment variables and sensitivity in XML documentation.
- Avoid several attributes that represent the same concept with slightly different spellings.
- Let generated help expose the resolved CLI surface; the attribute name is not a substitute for
  operator-facing documentation.

## Next

- [Build operational policy middleware](operational-policy-middleware.md)
- [Package command capabilities as assemblies](package-command-capabilities.md)
- [Extensibility reference](../explanation/extensibility.md)
