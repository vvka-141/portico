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
    /// True iff this option's declared type is a flag-arity type — <see cref="CliFlag"/> or its
    /// nullable variant <c>CliFlag?</c> (see <c>CliFlag.IsFlagType</c>). <see cref="bool"/> is NOT
    /// flag-arity: it routes through the scalar materializer. The short-option preprocessor uses this
    /// to decide whether a combined token like <c>-abc</c> can be safely split. Default <c>false</c>;
    /// implementations that know their declared type override.
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

    /// <summary>
    /// The environment variable this option falls back to, or <see langword="null"/> when it
    /// declares none. This is the variable's <b>name</b> — a declaration in source, safe to print.
    /// Its <b>value</b> is read only on the binding path and must never reach the help surface, for
    /// a sensitive option or any other: a variable nobody marked sensitive can still hold something
    /// its author did not anticipate (POR-149). Default <see langword="null"/>; implementations that
    /// know their attribute override.
    /// </summary>
    string? EnvironmentVariable => null;


    public sealed bool IsIn(CliInvocation invocation)
    {
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