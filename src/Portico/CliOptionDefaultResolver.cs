using System;
using System.ComponentModel;

namespace Portico;

/// <summary>
/// Resolves whether a bound member is optional and, if so, the default value the framework injects
/// when the <c>[CliOption]</c> is absent. Extracted from <see cref="CliOptionAttribute"/>
/// (SOL-79 / ROADMAP C2) so the resolution rules are unit-testable in isolation. The attribute's
/// virtual <c>CanAccept</c> binder seam is preserved by threading it in as the
/// <see cref="TryGetConverter"/> delegate rather than relocating it.
/// </summary>
internal static class CliOptionDefaultResolver
{
    /// <summary>Delegate shape of <see cref="CliOptionAttribute.CanAccept"/> — the virtual seam.</summary>
    public delegate bool TryGetConverter(Type type, out TypeConverter converter);

    /// <summary>
    /// Optional iff the member is nullable, has a C#-supplied default
    /// (<paramref name="hasReflectedDefault"/>), or the attribute supplies an
    /// <paramref name="attributeDefault"/> string. Reflected defaults win over attribute defaults;
    /// attribute defaults are converted through <paramref name="tryGetConverter"/> and surfaced as a
    /// <see cref="CliConfigurationException"/> on conversion failure.
    /// </summary>
    public static bool Resolve(
        Type type,
        string memberKind,
        string? memberName,
        bool isNullable,
        bool hasReflectedDefault,
        object? reflectedDefault,
        string? attributeDefault,
        TryGetConverter tryGetConverter,
        out object? defaultValue)
    {
        defaultValue = null;

        var optional = hasReflectedDefault || isNullable || attributeDefault is not null;
        if (!optional) return false;

        // C#-level default wins (e.g. `int n = 5`).
        if (hasReflectedDefault && reflectedDefault is not null && reflectedDefault is not DBNull)
        {
            defaultValue = reflectedDefault;
            return true;
        }

        // Nullable with no attribute default → null is the legal default.
        if (attributeDefault is null) return true;

        if (!tryGetConverter(type, out var valueConverter))
        {
            throw new CliConfigurationException(
                $"The {memberKind} '{memberName}' of type '{type.FullName}' is incompatible with the applied CLI attribute. " +
                $"Verify that the {memberKind} type supports conversion from CLI options and that any default values are properly defined.");
        }

        try
        {
            defaultValue = valueConverter.ConvertFromInvariantString(attributeDefault);
            return true;
        }
        catch (Exception e)
        {
            throw new CliConfigurationException(
                $"The default value '{attributeDefault}' cannot be assigned to the {memberKind} '{memberName}' of type '{type.FullName}'. " +
                $"Ensure the default value matches the expected type and format. Additional details: {e.Message}",
                e);
        }
    }
}
