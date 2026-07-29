using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;

using Portico.Reflection;

namespace Portico;

/// <summary>
/// Attribute to define command line options for a method or property.
/// </summary>
/// <example><code>
/// [CliRoute("greet")]
/// [CliCommandExample("greet --name Ada", "greet a user by name")]
/// public int Greet([CliOption("--name|-n", "the user to greet")] string name) =&gt; 0;
/// </code></example>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public class CliOptionAttribute : Attribute
{
    // Spec-string parsing + runtime matching are delegated to this mechanism (SOL-79 / ROADMAP C2)
    // so the attribute stays declarative metadata + documented virtual binder seams.
    private readonly CliOptionSpec _spec;

    /// <summary>
    /// Constructs a CLI option attribute with specified options and an optional description.
    /// </summary>
    /// <param name="specification">A pipe-separated string containing different CLI options.</param>
    /// <param name="description">A description of what the options do.</param>
    /// <exception cref="ArgumentException">Thrown when the options format is invalid or contains duplicates.</exception>
    public CliOptionAttribute(string specification, string description = "")
    {
        _spec = CliOptionSpec.Parse(specification);

        Description = description;
        Aliases = _spec.Aliases;
        LongOptionNames = _spec.LongOptionNames;
        ShortOptionNames = _spec.ShortOptionNames;
        PipeSeparatedAliases = _spec.PipeSeparatedAliases;

        // User-facing comma-separated form, long aliases first, longest within a group first.
        DisplayAliases = FormatAliasList(Aliases);
    }

    /// <summary>
    /// Specification of options as used in the CLI.
    /// </summary>
    public string PipeSeparatedAliases { get; }

    /// <summary>
    /// User-facing comma-separated alias list — long form first, longest within a group first
    /// (e.g. <c>--verbose, -v</c>). Preferred over <see cref="PipeSeparatedAliases"/> for anything
    /// the end user reads (help output, error messages). The pipe-separated form remains the
    /// canonical identifier that round-trips through <see cref="CliOptionAttribute(string, string)"/>.
    /// </summary>
    public string DisplayAliases { get; }

    /// <summary>
    /// Renders an arbitrary alias sequence with the same long-first, comma-separated convention
    /// used by <see cref="DisplayAliases"/>. Kept static so callers that hold aliases as
    /// <see cref="System.Collections.Immutable.ImmutableArray{T}"/> (e.g. the help renderer) can
    /// share the format without materializing a <see cref="CliOptionAttribute"/>.
    /// </summary>
    /// <example><code>
    /// string display = CliOptionAttribute.FormatAliasList(new[] { "-v", "--verbose" }); // "--verbose, -v"
    /// </code></example>
    public static string FormatAliasList(IEnumerable<string> aliases)
    {
        if (aliases is null) return string.Empty;
        return aliases
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .OrderBy(a => a.StartsWith("--", StringComparison.Ordinal) ? 0 : 1)
            .ThenByDescending(a => a.Length)
            .Join(", ");
    }

    /// <summary>
    /// Description of the CLI options.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Every alias declared in the constructor specification, <strong>with its leading dashes</strong>
    /// (e.g. <c>--name</c>, <c>-n</c>), in declaration order. Use <see cref="DisplayAliases"/> for
    /// user-facing rendering and <see cref="PipeSeparatedAliases"/> for the canonical round-trip form.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>
    /// Short alias names (declared with a single <c>-</c>), dash-stripped (e.g. <c>v</c> for <c>-v</c>).
    /// </summary>
    public IReadOnlyList<string> ShortOptionNames { get; }

    /// <summary>
    /// Long alias names (declared with <c>--</c>), dash-stripped (e.g. <c>verbose</c> for <c>--verbose</c>).
    /// </summary>
    public IReadOnlyList<string> LongOptionNames { get; }

    /// <summary>
    /// Whether collection-shaped options accept comma-separated values in a single token
    /// (e.g. <c>--items a,b,c</c>). Default <see langword="true"/>; override on a derived
    /// attribute to disable CSV splitting for option types whose values may legitimately
    /// contain commas (file paths, free-form text).
    /// </summary>
    public virtual bool AllowsCsv => true;

    /// <summary>
    /// String form of the default value applied when the option is absent and the bound
    /// member is optional. Converted via the option's type converter at materialization time;
    /// a conversion failure surfaces as <see cref="CliConfigurationException"/>. A C#-supplied
    /// default on the parameter wins over this value.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Name of an environment variable consulted when the option is absent from the command line.
    /// The command line always wins; the environment beats the default. Config layering for a
    /// containerized service, without a config file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Scalar</b> — the variable's value is converted exactly as a typed one would be, through the
    /// option's type converter.
    /// </para>
    /// <para>
    /// <b>Flag</b> (<see cref="CliFlag"/>) — the flag is on when the variable holds anything other
    /// than an empty string, <c>0</c>, <c>false</c> or <c>no</c> (any case). Set-but-empty is off:
    /// <c>docker run -e FOO</c> and an undefined variable in a compose file both pass <c>FOO=</c>, and
    /// silently turning a flag on because of that would be indefensible.
    /// </para>
    /// <para>
    /// <b>Collection</b> — comma-separated (<c>PORTICO_TAGS=a,b,c</c>). A value that itself contains a
    /// comma cannot be expressed this way; pass it on the command line.
    /// </para>
    /// <para>
    /// <b>Map</b> — <b>not supported, and it says so at startup.</b> One variable cannot carry
    /// key/value pairs without nesting one separator inside another, and every such encoding breaks on
    /// the first value that contains either. Setting this on a map option throws
    /// <see cref="CliConfigurationException"/> from <see cref="CliApplication.Create"/> rather than
    /// binding nothing at dispatch.
    /// </para>
    /// </remarks>
    /// <example><code>
    /// [CliOption("--token", EnvironmentVariable = "PORTICO_API_TOKEN")] string? token = null
    /// [CliOption("--verbose", EnvironmentVariable = "PORTICO_VERBOSE")] CliFlag? verbose = null
    /// [CliOption("--tag", EnvironmentVariable = "PORTICO_TAGS")] List&lt;string&gt;? tags = null
    /// </code></example>
    public string? EnvironmentVariable { get; init; }

    /// <summary>
    /// Marks the option's value as a secret. Its value is replaced with <c>***</c> everywhere the
    /// framework renders the command line — trace/timing output, and option-conversion errors.
    /// The value still binds normally; only the rendering changes.
    /// <para>
    /// Marking is explicit, never inferred from the option's name. A heuristic that guesses right
    /// most of the time teaches users to trust it, and the times it guesses wrong put a credential
    /// in a log.
    /// </para>
    /// </summary>
    /// <example><code>
    /// [CliRoute("db migrate")]
    /// [CliCommandExample("db migrate --connection-string \"Host=db\"")]
    /// int Migrate([CliOption("--connection-string", Sensitive = true)] string connectionString);
    /// </code></example>
    public bool Sensitive { get; init; }


    /// <summary>
    /// Returns a string representation of the option specification.
    /// </summary>
    /// <returns>A string that represents the option specification.</returns>
    /// <example><code>
    /// var option = new CliOptionAttribute("--name|-n");
    /// string spec = option.ToString(); // "--name|-n"
    /// </code></example>
    public override string ToString() => PipeSeparatedAliases;

    /// <summary>
    /// Tests whether a CLI string can be bound to <paramref name="optionType"/>, returning
    /// the <see cref="TypeConverter"/> the framework will use at materialization time. Recognizes
    /// <see cref="TimeSpan"/> and <see cref="CancellationToken"/> specially; falls back to the
    /// type's discovered <see cref="TypeDescriptor.GetConverter(Type)"/>; for collection- or
    /// dictionary-shaped types tries the element/value type recursively. Override on a derived
    /// attribute to widen acceptance for a custom option type.
    /// </summary>
    /// <param name="optionType">The target member type.</param>
    /// <param name="converter">The converter to use for scalar values; for collection-shaped
    /// types this is the element converter.</param>
    /// <returns><see langword="true"/> if a usable converter was found.</returns>
    /// <example><code>
    /// var option = new CliOptionAttribute("--count|-c");
    /// if (option.CanAccept(typeof(int), out var converter)) { /* int values bind */ }
    /// </code></example>
    public virtual bool CanAccept(Type optionType, out TypeConverter converter)
    {
        // Unwrap for the special-cased types ONLY. `TimeSpan?` is the form an *optional* timeout
        // must take, and an exact `== typeof(TimeSpan)` test misses it — so the human-readable
        // converter ("30 seconds", "5 min", "PT30S") was silently absent from the very case that
        // needs it most, leaving the BCL converter that accepts only "00:00:30".
        //
        // The default converter below is still resolved from the ORIGINAL type: for every other
        // nullable (int?, bool?, an enum?) TypeDescriptor already returns a NullableConverter that
        // handles null/empty correctly, and swapping that for the underlying type's converter would
        // change behaviour well beyond this fix. CliArgumentAttribute.CanAccept unwraps wholesale;
        // the option path carries collections and dictionaries too, so it stays conservative.
        var effectiveType = Nullable.GetUnderlyingType(optionType) ?? optionType;

        // IL2026/IL2067: TypeDescriptor.GetConverter uses reflection; the public entry point
        // (CliApplication.Create) already warns consumers about trimming incompatibility.
#pragma warning disable IL2026, IL2067
        converter = TypeDescriptor.GetConverter(optionType);
#pragma warning restore IL2026, IL2067

        if (effectiveType == typeof(TimeSpan))
        {
            converter = new MultiFormatTimeSpanConverter();
            ThrowIf.False(converter.CanConvertFrom(typeof(string)));
        }
        else if (effectiveType == typeof(CancellationToken))
        {
            converter = new CliCancellationTokenTypeConverter();
            ThrowIf.False(converter.CanConvertFrom(typeof(string)));
        }

        if (converter.SupportsCliOperandConversion())
        {
            return true;
        }

        var valueConverter = optionType
            .GetGenericDictionaryArgumentTypes()
            .Where(args => args.Key == typeof(string))
            .Select(args => args.Value)
            .Union(optionType.GetGenericEnumerableArgumentTypes())
            .SelectMany(type =>
            {
                if (CanAccept(type, out var valueConverter))
                {
                    return [valueConverter];
                }

                return Enumerable.Empty<TypeConverter>();
            })
            .FirstOrDefault();

        if (valueConverter is not null)
        {
            converter = valueConverter;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the comparer used for map-option keys (the <c>[key]</c> in <c>--cfg[key] value</c>).
    /// Default is <see cref="StringComparer.Ordinal"/> — map keys are <strong>case-sensitive</strong>,
    /// matching the common expectation that <c>--env[Region]</c> and <c>--env[region]</c> are
    /// distinct entries. Override on a derived attribute to opt into case-insensitive keys
    /// (<see cref="StringComparer.OrdinalIgnoreCase"/>).
    /// </summary>
    /// <example><code>
    /// var option = new CliOptionAttribute("--cfg");
    /// StringComparer comparer = option.GetValueComparer(); // Ordinal (case-sensitive) by default
    /// </code></example>
    public virtual StringComparer GetValueComparer() => StringComparer.Ordinal;


    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="optionName"/> (with leading dashes)
    /// matches any of this attribute's declared aliases.
    /// </summary>
    /// <example><code>
    /// var option = new CliOptionAttribute("--name|-n");
    /// bool matched = option.IsMatch("-n"); // true
    /// </code></example>
    [DebuggerNonUserCode]
    public bool IsMatch(string optionName) => _spec.IsMatch(optionName);

    /// <summary>
    /// Determines whether the bound <paramref name="parameter"/> is optional and resolves the
    /// default value the framework will inject when the option is absent. Optional iff the
    /// parameter is nullable, has a C#-supplied default, or this attribute supplies
    /// <see cref="DefaultValue"/>. The reflected default wins over the attribute default.
    /// </summary>
    /// <example><code>
    /// var option = new CliOptionAttribute("--retries") { DefaultValue = "3" };
    /// bool optional = option.IsOptional(parameter, out object? fallback); // true, fallback = 3
    /// </code></example>
    public bool IsOptional(ParameterInfo parameter, out object? defaultValue) =>
        CliOptionDefaultResolver.Resolve(
            type: parameter.ParameterType,
            memberKind: "parameter",
            memberName: parameter.Name,
            isNullable: parameter.IsNullable(),
            hasReflectedDefault: parameter.HasDefaultValue,
            reflectedDefault: parameter.DefaultValue,
            attributeDefault: DefaultValue,
            tryGetConverter: CanAccept,
            out defaultValue);

    /// <summary>
    /// Determines whether the bound <paramref name="property"/> is optional and resolves the
    /// default value the framework will inject when the option is absent. Optional iff the
    /// property is nullable or this attribute supplies <see cref="DefaultValue"/>; properties
    /// have no reflected default to fall back on.
    /// </summary>
    /// <example><code>
    /// var option = new CliOptionAttribute("--verbose") { DefaultValue = "false" };
    /// bool optional = option.IsOptional(property, out object? fallback); // true
    /// </code></example>
    public bool IsOptional(PropertyInfo property, out object? defaultValue) =>
        CliOptionDefaultResolver.Resolve(
            type: property.PropertyType,
            memberKind: "property",
            memberName: property.Name,
            isNullable: property.IsNullable(),
            hasReflectedDefault: false,
            reflectedDefault: null,
            attributeDefault: DefaultValue,
            tryGetConverter: CanAccept,
            out defaultValue);
}