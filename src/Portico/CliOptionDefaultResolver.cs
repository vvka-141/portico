using System;
using System.ComponentModel;
using System.Linq;

using Portico.Reflection;

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

        // A map default cannot be expressed as one string. Refused rather than converted, and
        // refused LOUDLY: it used to be accepted and then silently ignored, so a
        // `DefaultValue = "a=1"` on a Dictionary bound an empty map and the author had no way to
        // know (POR-156). The reasoning is POR-54's, which already declined EnvironmentVariable on
        // map options for the same reason — every encoding of key/value pairs in one string nests
        // one separator inside another and breaks on the first value containing either.
        if (type.GetGenericDictionaryArgumentTypes().Any())
        {
            throw new CliConfigurationException(
                $"The {memberKind} '{memberName}' is a map, and DefaultValue is not supported on map " +
                $"options — one string cannot carry key/value pairs without an encoding that breaks " +
                $"on the first value containing a separator. Give the {memberKind} a C# default, or " +
                $"populate it in the handler when it arrives empty.");
        }

        // A collection default is one authored string that has to carry several values, which is
        // exactly the problem the environment-variable path already solved: split on a comma
        // (POR-73). Same problem, same answer — and the same escape hatch, since a value containing
        // a comma has to come from argv.
        //
        // Without this the string was converted by the ELEMENT converter — CanAccept answers on the
        // element for a collection type — so `DefaultValue = "eu,us"` on a string[] produced a
        // string, and MethodInfo.Invoke rejected it at exit 1 (POR-156). An int[] failed earlier and
        // more confusingly still, with "1,2 is not a valid value for Int32".
        if (CliCollectionOptionMaterializer.GetCollectionItemType(type) is { } itemType &&
            CliCollectionOptionMaterializer.BuildCollectionFactory(type, itemType) is { } collectionFactory &&
            tryGetConverter(itemType, out var itemConverter))
        {
            var items = attributeDefault
                .Split(',')
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Select(item => Convert(itemConverter, item, itemType))
                .ToArray();

            defaultValue = collectionFactory(items);
            return true;
        }

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

        object Convert(TypeConverter converter, string item, Type target)
        {
            try
            {
                return converter.ConvertFromInvariantString(item)
                       ?? throw new FormatException($"'{item}' converted to null.");
            }
            catch (Exception e)
            {
                throw new CliConfigurationException(
                    $"The default value '{attributeDefault}' for the {memberKind} '{memberName}' is a " +
                    $"comma-separated list, and its element '{item}' is not a valid " +
                    $"{target.Name}. Additional details: {e.Message}",
                    e);
            }
        }
    }
}
