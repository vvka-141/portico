using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Portico.Reflection;

internal sealed class CliOptionParameterInfo : CliParameterInfo, ICliOptionMemberInfo
{
    // The interface contract guarantees a non-null name; the inherited
    // ParameterInfoDecorator.Name is string? (matching reflection). Explicit
    // interface implementation null-coalesces; in practice the underlying
    // ParameterInfo.Name is never null for a real C# method parameter.
    string ICliOptionMemberInfo.Name => Name ?? string.Empty;

    private readonly CliOptionAttribute _optionAttribute;
    private readonly CliOptionMaterializer _materializer;

    public CliOptionParameterInfo(ParameterInfo parameter) 
        : base(parameter)
    {
        var attributes = GetCustomAttributes(true).OfType<Attribute>().ToArray();
        _optionAttribute = attributes
            .OfType<CliOptionAttribute>()
            .Union([new CliOptionAttribute($"--{parameter.Name}")])
            .First();

        if (_optionAttribute.IsOptional(parameter, out object? defaultValue))
        {
            IsOptional = true;
            DefaultValue = defaultValue;
        }
        else
        {
            IsOptional = false;
            DefaultValue = null;
        }
        
 
        _materializer = CliOptionMaterializer.CreateOrThrow(
            _optionAttribute, 
            parameter.ParameterType, 
            IsOptional, 
            DefaultValue);


        Aliases = [.. _optionAttribute.Aliases];
        Description = attributes
            .OfType<DescriptionAttribute>()
            .Select(d => d.Description)
            .Union([_optionAttribute.Description])
            .FirstOrDefault("");
    }

    public string PipeSeparatedAliases => _optionAttribute.PipeSeparatedAliases;

    public string Description { get; }

    public bool IsMatch(string optionName) => _optionAttribute.IsMatch(optionName);

    public ImmutableArray<string> Aliases { get; }

    public bool IsFlagArity => CliFlag.IsFlagType(ParameterType, out _);

    public bool IsMapArity => ParameterType.GetGenericDictionaryArgumentTypes().Any();

    public bool IsSensitive => _optionAttribute.Sensitive;

    public string? EnvironmentVariable => _optionAttribute.EnvironmentVariable;

    public override bool IsOptional { get; }

    public override object? DefaultValue { get; }


    public override object? Materialize(CliInvocation invocation)
    {
        var value = _materializer.Materialize(invocation);
        CliValidation.ValidateParameterValue(this, value, _optionAttribute.DisplayAliases);
        return value;
    }

    public override string ToString() => _optionAttribute.PipeSeparatedAliases;
}