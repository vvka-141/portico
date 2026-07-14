using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Portico.Reflection;

internal sealed class CliOptionsPropertyInfo : PropertyInfoDecorator,  ICliOptionMemberInfo
{
    private readonly CliOptionAttribute _optionAttribute;
    private readonly CliOptionMaterializer _materializer;

    public CliOptionsPropertyInfo(PropertyInfo property) : base(property)
    {
        var attributes = property.GetCustomAttributes().ToList();
        _optionAttribute = attributes.OfType<CliOptionAttribute>().Single();

        if (_optionAttribute.IsOptional(property, out object? defaultValue))
        {
            IsOptional = true;
            DefaultValue = defaultValue;
        }
        else
        {
            IsOptional = false;
            DefaultValue = null;
        }


        Description = attributes
            .OfType<DescriptionAttribute>()
            .Select(a => a.Description)
            .Union([_optionAttribute.Description])
            .First();
        Aliases = [.. _optionAttribute.Aliases];
        _materializer = CliOptionMaterializer.CreateOrThrow(_optionAttribute, property.PropertyType, IsOptional, null);
    }


    public string Description { get; }

    public string PipeSeparatedAliases => _optionAttribute.PipeSeparatedAliases;

    public bool IsOptional { get; }
    public object? DefaultValue { get; }

    public ImmutableArray<string> Aliases { get; }



    public bool IsMatch(string optionName) => _optionAttribute.IsMatch(optionName);

    public bool IsFlagArity => CliFlag.IsFlagType(PropertyType, out _);

    public bool IsSensitive => _optionAttribute.Sensitive;

    public static bool IsBundleProperty(PropertyInfo propertyInfo)
    {
        return propertyInfo.GetCustomAttribute<CliOptionAttribute>() is not null;
    }

    public object? Materialize(CliInvocation invocation)
    {
        var value = _materializer.Materialize(invocation);
        CliValidation.ValidatePropertyValue(this, value, _optionAttribute.DisplayAliases);
        return value;
    }

    public override string ToString() => _optionAttribute.PipeSeparatedAliases;
}