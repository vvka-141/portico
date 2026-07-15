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

    /// <summary>
    /// True iff this option's declared type is a map (a generic <see cref="System.Collections.Generic.IDictionary{TKey,TValue}"/>),
    /// bound from the <c>--env[key] value</c> syntax. The short-option preprocessor uses this to
    /// leave a short map token (<c>-e[key]</c>) unsplit so its bracket key survives to the
    /// tokenizer. Default <c>false</c>; implementations that know their declared type override.
    /// </summary>
    bool IsMapArity => false;

    /// <summary>
    /// True iff the option is declared <c>Sensitive = true</c>. The framework renders its value as
    /// <c>***</c> instead of echoing it. Default <c>false</c>.
    /// </summary>
    bool IsSensitive => false;


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