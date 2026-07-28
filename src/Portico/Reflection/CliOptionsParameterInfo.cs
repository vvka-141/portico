using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace Portico.Reflection;

internal sealed class CliOptionsParameterInfo : CliParameterInfo
{
    private readonly ImmutableArray<CliOptionsPropertyInfo> _properties;
    // IL2072/IL2075: bundle instantiation and property discovery use reflection;
    // CliApplication.Create warns consumers about trimming incompatibility.
#pragma warning disable IL2072, IL2075
    public CliOptionsParameterInfo(ParameterInfo parameter) : base(parameter)
    {
        ThrowIf.False(CliOptions.IsAssignableFrom(parameter.ParameterType));
        CliOptionsType = parameter.ParameterType;

        // Bundles are instantiated per-invocation via Activator.CreateInstance. A missing
        // parameterless ctor would fail at the first command invocation with MissingMethodException
        // — catch it at config time with an actionable message instead.
        if (CliOptionsType.IsAbstract ||
            CliOptionsType.GetConstructor(BindingFlags.Instance | BindingFlags.Public, []) is null)
        {
            throw new CliConfigurationException(
                $"Option bundle type '{CliOptionsType.FullName}' must be a concrete class with a " +
                "public parameterless constructor. Bundles are instantiated per-command-invocation " +
                "and cannot require constructor arguments. Move any dependencies out of the bundle " +
                "or expose them via [CliOption] properties.");
        }

        var properties = new List<CliOptionsPropertyInfo>();
        foreach (var p in CliOptionsType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            try
            {
                if (CliOptionsPropertyInfo.IsBundleProperty(p))
                    properties.Add(new CliOptionsPropertyInfo(p));
            }
            catch (Exception ex) when (ex is ArgumentException || ex.InnerException is ArgumentException)
            {
                var specEx = ex.InnerException as ArgumentException ?? (ArgumentException)ex;
                throw new CliConfigurationException(
                    $"Property '{p.Name}' on bundle type '{CliOptionsType.Name}': " +
                    CliOptionMaterializer.Explain(specEx));
            }
        }
        _properties = [.. properties];
    }
#pragma warning restore IL2072, IL2075

    public Type CliOptionsType { get; }


    public IEnumerable<CliOptionsPropertyInfo> GetOptions() => _properties;


    public override object? Materialize(CliInvocation invocation)
    {
#pragma warning disable IL2072
        var bundle = Activator.CreateInstance(CliOptionsType);
#pragma warning restore IL2072
        foreach (var property in _properties)
        {
            var value = property.Materialize(invocation);
            if (value is null && property.IsOptional)
            {
                value = property.GetValue(bundle);
                if (value is not null)
                {
                    continue;
                }
            }
            property.SetValue(bundle, value);

        }

        // Per-property validation already ran inside property.Materialize. This call picks up
        // class-level attributes and cross-property rules (IValidatableObject.Validate); we skip
        // property-level attrs here (validateAllProperties: false) to avoid duplicate messages.
        if (bundle is not null)
        {
            CliValidation.ValidateBundle(bundle);
        }
        return bundle;
    }
}