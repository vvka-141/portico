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
    }

    public string Description { get; }

    public TypeConverter TypeConverter { get; }

    public int CliRoutePosition { get; }

    public string CliArgumentName => _argument.Name;

    public override object? Materialize(CliInvocation invocation)
    {
        if (CliRoutePosition >= invocation.Segments.Length)
        {
            throw new CliExitException(
                $"Missing positional argument <{CliArgumentName.ToUpperInvariant()}>.")
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
            throw new CliExitException(
                $"Argument <{CliArgumentName.ToUpperInvariant()}>: value '{rawValue}' could not be converted. {e.Message}")
            { ExitCode = CliExitException.UsageErrorExitCode };
        }

        CliValidation.ValidateParameterValue(
            this,
            converted,
            $"<{CliArgumentName.ToUpperInvariant()}>");
        return converted;
    }
}
