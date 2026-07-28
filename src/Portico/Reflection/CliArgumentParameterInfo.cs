using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Portico.Reflection;

internal sealed class CliArgumentParameterInfo : CliParameterInfo
{
    private readonly CliArgumentAttribute _argument;

    public CliArgumentParameterInfo(
        ParameterInfo parameter,
        CliArgumentAttribute argument,
        int cliRoutePosition) : base(parameter)
    {
        _argument = argument;
        if (false == argument.CanAccept(parameter.ParameterType, out var typeConverter))
        {
            throw new CliConfigurationException(
                $"Parameter '{parameter.Name}' of type '{parameter.ParameterType.FullName}' cannot be bound " +
                $"as a CLI argument: no type converter accepts string input for this type.");
        }

        if (cliRoutePosition < 0)
        {
            throw new CliConfigurationException(
                $"The [CliArgument(\"{argument.ParameterName}\", ...)] attribute on parameter '{parameter.Name}' " +
                $"is not placed in the method's route. Declare it inline with the [CliRoute] attributes in the " +
                $"order the argument should appear on the command line.");
        }

        var attributes = GetCustomAttributes(true).ToArray();

        TypeConverter = typeConverter;
        Description = attributes
            .OfType<DescriptionAttribute>()
            .Select(a => a.Description)
            .FirstOrDefault(argument.Description)
            .DefaultIfNullOrWhiteSpace(argument.Name);
        CliRoutePosition = cliRoutePosition;

        // A C# default on the parameter — `string target = "default"` — is the author stating,
        // unambiguously, that the positional is optional. Honour it: the route matches with the
        // segment absent and the parameter binds to its default.
        IsOptionalArgument = HasDefaultValue;
        ArgumentDefaultValue = HasDefaultValue ? DefaultValue : null;
    }

    public string Description { get; }

    /// <summary>True when the bound parameter declares a C# default value.</summary>
    public bool IsOptionalArgument { get; }

    /// <summary>The parameter's C# default, used when an optional positional is omitted.</summary>
    public object? ArgumentDefaultValue { get; }

    public TypeConverter TypeConverter { get; }

    public int CliRoutePosition { get; }

    public string CliArgumentName => _argument.Name;

    public override object? Materialize(CliInvocation invocation)
    {
        if (CliRoutePosition >= invocation.Segments.Length)
        {
            if (IsOptionalArgument)
            {
                return ArgumentDefaultValue;
            }

            // Argument name is developer-supplied, but sanitize on the same choke-point principle as
            // the option path — every framework-composed error string on this boundary goes through
            // CliSanitizer (POR-60, parity with CliOptionMaterializationException).
            throw new CliExitException(
                CliSanitizer.Sanitize($"Missing positional argument <{CliArgumentName.ToUpperInvariant()}>."))
            { ExitCode = CliExitException.UsageErrorExitCode };
        }

        var rawValue = invocation.Segments[CliRoutePosition];
        object? converted;
        try
        {
            converted = TypeConverter.ConvertFromInvariantString(rawValue);
        }
        catch (Exception e)
        {
            // rawValue and e.Message are both attacker-influenced (argv). Sanitize the whole composed
            // message so an argument error cannot carry ANSI escapes / zero-width / control characters
            // to the terminal — the parity gap the POR-48 sanitizer doc warned about (POR-60).
            throw new CliExitException(
                CliSanitizer.Sanitize(
                    $"Argument <{CliArgumentName.ToUpperInvariant()}>: value '{rawValue}' could not be converted. {CliOptionMaterializer.Explain(e)}"))
            { ExitCode = CliExitException.UsageErrorExitCode };
        }

        CliValidation.ValidateParameterValue(
            this,
            converted,
            $"<{CliArgumentName.ToUpperInvariant()}>");
        return converted;
    }
}
