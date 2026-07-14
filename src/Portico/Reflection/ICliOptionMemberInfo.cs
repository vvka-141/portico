using System.Collections.Immutable;

namespace Portico.Reflection;

internal interface ICliOptionMemberInfo
{
    string Name { get; }

    string PipeSeparatedAliases { get; }
    bool IsMatch(string optionName);

    bool IsOptional { get; }

    object? DefaultValue { get; }

    ImmutableArray<string> Aliases { get; }
    string Description { get; }

    /// <summary>
    /// True iff this option's declared type is a flag-arity type (<see cref="CliFlag"/>, its
    /// nullable variant, <see cref="bool"/>, or <c>bool?</c>). The short-option preprocessor uses
    /// this to decide whether a combined token like <c>-abc</c> can be safely split. Default
    /// <c>false</c>; implementations that know their declared type override.
    /// </summary>
    bool IsFlagArity => false;


    public sealed bool IsIn(CliInvocation invocation)
    {
        //Debug.WriteLine(Name);
        foreach (var option in invocation.Options)
        {
            if (IsMatch(option.Name))
            {
                return true;
            }
        }

        return false;
    }




    bool IsNotIn(CliInvocation invocation) => (false == IsIn(invocation));
}