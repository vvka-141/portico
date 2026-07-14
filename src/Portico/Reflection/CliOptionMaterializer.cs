using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Portico.Reflection;

/// <summary>
/// Internal base for option materializers. The base was once <c>public abstract</c> while every
/// concrete subclass was internal — a public surface promising extensibility the framework never
/// honoured (the <c>CreateOrThrow</c> cascade only ever dispatches to the built-in subclasses).
/// <para>
/// <b>ROADMAP item C4 — RESOLVED: this stays internal. There is no <c>WithMaterializer&lt;T&gt;</c>.</b>
/// The question was whether to expose a materializer seam or seal it. Sealed, because the extension
/// points a user actually needs already exist and cost the framework nothing:
/// </para>
/// <list type="number">
///   <item>
///     <b><c>[TypeConverter]</c></b> — the BCL's own answer to "my type is not convertible from a
///     string". A user's domain type binds as a <c>[CliOption]</c> today with no framework seam at
///     all; <see cref="CliOptionAttribute.CanAccept"/> resolves it through
///     <c>TypeDescriptor.GetConverter</c>. Verified, not assumed.
///   </item>
///   <item>
///     <b>Subclassing <see cref="CliOptionAttribute"/> / <see cref="CliArgumentAttribute"/></b> and
///     overriding <c>CanAccept</c> — for a user who needs to change converter selection itself.
///   </item>
/// </list>
/// <para>
/// CHARTER §7 is directive here: "YAGNI applies hard… Speculative extensibility is rejected. If we
/// proposed a hook once and nobody asked for it again, delete the proposal; don't ship the hook."
/// No user has hit a wall these two points cannot solve. Exposing a seam later is additive and safe;
/// removing one is a breaking change — so the asymmetry says wait for a named scenario.
/// </para>
/// <para>
/// <b>Do not make this public without reopening C4</b> — <c>Portico_PublicSurface_Should</c> has a
/// test that fails if you do. See <c>docs/explanation/extensibility.md</c> and <c>docs/ROADMAP.md</c>.
/// </para>
/// </summary>
internal abstract class CliOptionMaterializer
{
    /// <summary>
    /// Materializes an object from the given CLI command line.
    /// </summary>
    public abstract object? Materialize(CliInvocation invocation);

    /// <summary>
    /// Shared hint for the "this option cannot take that token" errors. Portico resolves a
    /// positional that follows an option only via the POSIX <c>--</c> terminator — the greedy
    /// tokenizer otherwise binds trailing bare tokens to the preceding option (see the CommandLine
    /// ROADMAP: positional-after-options is a documented 2.0 behavior, not implicit). Naming the
    /// offending token plus the terminator turns an opaque arity/type error into an actionable one
    /// (SOL-77).
    /// </summary>
    protected static string PositionalAfterOptionHint(string token) =>
        $"If '{token}' is a positional argument, pass it after the '--' terminator (e.g. '… -- {token}').";

    /// <summary>
    /// The value of the option's <c>EnvironmentVariable</c>, or <see langword="null"/> when the
    /// attribute declares none or the variable is unset. Consulted only when the option is absent
    /// from the command line — argv always wins.
    /// </summary>
    protected static string? EnvironmentValue(CliOptionAttribute attribute) =>
        string.IsNullOrWhiteSpace(attribute.EnvironmentVariable)
            ? null
            : Environment.GetEnvironmentVariable(attribute.EnvironmentVariable!);

    /// <summary>
    /// Whether an environment variable's value turns a <b>flag</b> on. A flag carries no value on the
    /// command line, so the environment has to answer a question argv never asks: what does "present"
    /// mean when the mechanism is a string?
    /// </summary>
    /// <remarks>
    /// Set-but-empty is <b>off</b>, and so are <c>0</c>, <c>false</c> and <c>no</c> (any case).
    /// Everything else — including <c>1</c>, <c>true</c>, <c>yes</c>, and any other non-empty value —
    /// is on. Two failure modes drove this: <c>docker run -e FOO</c> and a compose file with an
    /// undefined variable both pass <c>FOO=</c>, so treating "set" as "on" would silently enable a
    /// flag nobody asked for; and <c>FOO=false</c> meaning "on" would be indefensible in any
    /// language (POR-54).
    /// </remarks>
    protected static bool IsEnvironmentFlagSet(string value) =>
        value.Trim() is var trimmed &&
        trimmed.Length > 0 &&
        !string.Equals(trimmed, "0", StringComparison.Ordinal) &&
        !string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(trimmed, "no", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The converter's own explanation of a failed conversion, with the internal parameter name
    /// stripped. A <see cref="TypeConverter"/> reports a bad value as an
    /// <see cref="ArgumentException"/>, whose <see cref="Exception.Message"/> appends
    /// <c>(Parameter 'value')</c> — and <c>value</c> is a local in this file, not anything the user
    /// typed or can act on. Keep the diagnosis ("abc is not a valid value for Decimal"), drop the
    /// leak (POR-17).
    /// </summary>
    protected static string Explain(Exception ex)
    {
        var message = ex.Message;

        if (ex is ArgumentException { ParamName: { Length: > 0 } paramName })
        {
            // Matching on the suffix the BCL actually builds from ParamName, rather than a regex
            // over the message, means a localized framework simply keeps its message intact instead
            // of being mangled by a pattern written for English.
            var suffix = $"(Parameter '{paramName}')";
            var index = message.IndexOf(suffix, StringComparison.Ordinal);
            if (index >= 0)
            {
                message = message.Remove(index, suffix.Length);
            }
        }

        return message.Trim();
    }

    /// <summary>
    /// Builds a materializer for the given attribute + declared type. Throws
    /// <see cref="CliConfigurationException"/> if no built-in handles the combination.
    /// </summary>
    public static CliOptionMaterializer CreateOrThrow(
        CliOptionAttribute attribute,
        Type declaredType,
        bool isOptional,
        object? defaultValue)
    {
        // Scalar is the terminal builder: it either returns a materializer or throws a
        // type-specific CliConfigurationException. The three leading Try* forms return null only
        // when they don't apply, so the cascade never yields null — no outer guard is needed.
        return
            CliDictionaryOptionMaterializer.TryCreateMaterializer(attribute, declaredType, isOptional) ??
            CliFlagOptionMaterializer.TryCreateMaterializer(attribute, declaredType, isOptional, defaultValue) ??
            CliCollectionOptionMaterializer.TryCreateMaterializer(attribute, declaredType, isOptional, defaultValue) ??
            CliScalarOptionMaterializer.Create(attribute, declaredType, isOptional, defaultValue);
    }
}

/// <summary>
/// Materializes key-value CLI options into a dictionary.
/// </summary>
internal sealed class CliDictionaryOptionMaterializer : CliOptionMaterializer
{
    private readonly CliOptionAttribute _attribute;
    private readonly Func<IDictionary> _factory;
    private readonly Func<string, string, object> _valueParser;   // (key, value) -> parsed value
    private readonly bool _isRequired;

    private CliDictionaryOptionMaterializer(
        CliOptionAttribute attribute,
        Func<IDictionary> factory,
        Func<string, string, object> valueParser,
        bool isOptional)
    {
        // A map option does not read the environment, and it says so at STARTUP rather than binding
        // nothing at dispatch (POR-54). Declining is deliberate: one variable would have to carry
        // key/value pairs, and every encoding for that (PORTICO_SHARD=eu=3,us=5) nests one separator
        // inside another and breaks on the first value containing either. An encoding nobody can
        // guess is worse than an honest refusal — and the refusal is loud, which is the actual bug
        // being fixed here: the attribute used to be silently inert on this shape.
        if (!string.IsNullOrWhiteSpace(attribute.EnvironmentVariable))
        {
            throw new CliConfigurationException(
                $"Option '{attribute.DisplayAliases}' is a map, and EnvironmentVariable is not supported " +
                $"on map options — one variable cannot carry key/value pairs without an encoding that " +
                $"breaks on the first value containing a separator. Bind the map from the command line " +
                $"(--opt[key] value), or take the environment value as a scalar option and parse it in " +
                $"your handler, where you can choose the format.");
        }

        _attribute = attribute;
        _factory = factory;
        _valueParser = valueParser;
        _isRequired = !isOptional;
    }


    /// <summary>
    /// Attempts to create a <see cref="CliDictionaryOptionMaterializer"/> if the declared type is supported.
    /// </summary>
    /// <param name="attribute">The CLI option attribute.</param>
    /// <param name="declaredType">The declared type.</param>
    /// <param name="isOptional">True if the dictionary parameter is optional.</param>
    /// <returns>A <see cref="CliDictionaryOptionMaterializer"/>, or <c>null</c> if not applicable.</returns>
    internal static CliDictionaryOptionMaterializer? TryCreateMaterializer(
        CliOptionAttribute attribute,
        Type declaredType,
        bool isOptional)
    {
        return declaredType
            .GetGenericDictionaryArgumentTypes()
            .SelectMany(pair =>
            {
                // pair: KeyValuePair<Type, Type>
                if (pair.Key == typeof(string) &&
                    attribute.CanAccept(pair.Value, out TypeConverter valueConverter))
                {
                    var valueType = pair.Value;
                    var comparer = attribute.GetValueComparer();
                    var targetType = typeof(Dictionary<,>).MakeGenericType([typeof(string), valueType]);
                    var ctor = targetType.GetConstructor([typeof(IEqualityComparer<string>)])!;
                    IDictionary CreateDictionary() => (IDictionary)ctor.Invoke([comparer]);

                    // Mirror the scalar/collection paths: wrap converter faults in a
                    // CliOptionMaterializationException so a bad map value lands on the usage-error
                    // boundary (exit 2, rich message) instead of leaking a raw FormatException at
                    // exit 1 (SOL-76). The key is named so the user can see which entry failed.
                    object ParseValue(string key, string value)
                    {
                        try
                        {
                            var parsed = valueConverter.ConvertFromInvariantString(value);
                            if (parsed is null)
                            {
                                throw new CliOptionMaterializationException(
                                    attribute.Sensitive
                                        ? $"Value for key '{key}' of option '{attribute.DisplayAliases}' could not be converted to '{valueType.Name}'."
                                        : $"Value '{value}' for key '{key}' of option '{attribute.DisplayAliases}' could not be converted to '{valueType.Name}'.");
                            }
                            return parsed;
                        }
                        catch (CliOptionMaterializationException)
                        {
                            throw;
                        }
                        catch (FormatException ex) when (valueType.IsEnum)
                        {
                            var expected = string.Join(", ", Enum.GetNames(valueType));
                            throw new CliOptionMaterializationException(
                                attribute.Sensitive
                                    ? $"Value for key '{key}' of option '{attribute.DisplayAliases}' is not a valid {valueType.Name}. Expected: {expected}."
                                    : $"Value '{value}' for key '{key}' of option '{attribute.DisplayAliases}' is not a valid {valueType.Name}. Expected: {expected}.",
                                ex);
                        }
                        catch (Exception ex)
                        {
                            throw new CliOptionMaterializationException(
                                attribute.Sensitive
                                    ? $"Value for key '{key}' of option '{attribute.DisplayAliases}' is invalid."
                                    : $"Value '{value}' for key '{key}' of option '{attribute.DisplayAliases}' is invalid. {Explain(ex)}",
                                ex);
                        }
                    }
                    return [new CliDictionaryOptionMaterializer(attribute, CreateDictionary, ParseValue, isOptional)];
                }

                return Enumerable.Empty<CliDictionaryOptionMaterializer>();
            })
            .SingleOrDefault();
    }

    /// <inheritdoc/>
    public override object? Materialize(CliInvocation invocation)
    {
        var options = invocation.Options
            .Where(option => _attribute.IsMatch(option.Name))
            .ToList();

        if (options.Count == 0 && _isRequired)
        {
            throw new CliOptionMaterializationException(
                $"The required option '{_attribute.DisplayAliases}' is missing. " +
                $"Supply at least one key/value, e.g. {SampleUsage()}.");
        }

        var result = _factory.Invoke();
        foreach (var option in options)
        {
            // Empty-key form (`--config[]`): surface a targeted usage error rather than a raw
            // "unrecognized option" or a silent miss (SOL-76).
            if (option is ICliMapOptionCapture { Key: var mapKey } && string.IsNullOrEmpty(mapKey))
            {
                throw new CliOptionMaterializationException(
                    $"The option '{_attribute.DisplayAliases}' was given an empty map key. Use {SampleUsage()}.");
            }

            switch (option)
            {
                case CliKeyValueOptionCapture capture:
                    // Duplicate key is a defined usage error (not last-wins): the dictionary uses
                    // the attribute's comparer, so this honors the configured key case-sensitivity.
                    if (result.Contains(capture.Key))
                    {
                        throw new CliOptionMaterializationException(
                            $"Duplicate key '{capture.Key}' for option '{_attribute.DisplayAliases}'. " +
                            $"Each map key may be specified only once.");
                    }
                    result.Add(capture.Key, _valueParser.Invoke(capture.Key, capture.Value));
                    break;

                case CliKeyCollectionOptionCapture collection:
                    throw new CliOptionMaterializationException(
                        $"The option '{_attribute.DisplayAliases}[{collection.Key}]' expected a single value but received several. Provide one value per key.");

                case CliKeyFlagOptionCapture flag:
                    throw new CliOptionMaterializationException(
                        $"The option '{_attribute.DisplayAliases}[{flag.Key}]' is missing its value. Use {SampleUsage()}.");

                default:
                    throw new CliOptionMaterializationException(
                        $"The option '{option.Name}' expected a key/value pair. Use {SampleUsage()}.");
            }
        }

        return result;
    }

    /// <summary>A copy-pasteable usage hint, e.g. <c>--config[key] value</c>.</summary>
    private string SampleUsage()
    {
        var longName = _attribute.LongOptionNames.FirstOrDefault();
        var sample = longName is null ? "--opt" : $"--{longName}";
        return $"{sample}[key] value";
    }
}

internal sealed class CliScalarOptionMaterializer : CliOptionMaterializer
{
    private readonly CliOptionAttribute _attribute;
    private readonly Func<string, object> _valueParser;
    private readonly bool _isOptional;
    private readonly object? _defaultValue;

    private CliScalarOptionMaterializer(
        CliOptionAttribute attribute,
        Func<string, object> valueParser,
        bool isOptional,
        object? defaultValue)
    {
        _attribute = attribute;
        _valueParser = valueParser;
        _isOptional = isOptional;
        _defaultValue = defaultValue;
    }
    public override object? Materialize(CliInvocation invocation)
    {
        var options = invocation.Options
            .Where(option => _attribute.IsMatch(option.Name))
            .ToList();
        if (options.Count > 1)
        {
            throw new CliOptionMaterializationException(
                $"The option '{_attribute.DisplayAliases}' was specified multiple times. Ensure only one instance of this option is provided.");
        }

        if (options.Count == 0)
        {
            // Environment-variable fallback (opt-in per attribute). Applies to scalar options only.
            if (!string.IsNullOrWhiteSpace(_attribute.EnvironmentVariable))
            {
                var envValue = Environment.GetEnvironmentVariable(_attribute.EnvironmentVariable);
                if (envValue is not null)
                {
                    return _valueParser.Invoke(envValue);
                }
            }

            if (_isOptional)
            {
                return _defaultValue;
            }

            throw new CliOptionMaterializationException(
                $"The required option '{_attribute.DisplayAliases}' is missing. Please provide this option and try again.");
        }

        var option = options.Single();
        return option switch
        {
            CliScalarOptionCapture capture => _valueParser.Invoke(capture.Value),
            ICliMapOptionCapture => throw new CliOptionMaterializationException(
                $"The option '{option.Name}' cannot accept a keyed map. Provide a single value instead."),
            // A scalar option that swallowed multiple bare tokens is the classic
            // positional-after-option surprise (`--output out.dll main.cs`): keep the first value,
            // flag the rest, and point at the `--` terminator (SOL-77).
            CliCollectionOptionCapture collection => throw new CliOptionMaterializationException(
                $"The option '{option.Name}' expects a single value but received {collection.Values.Length} " +
                $"({string.Join(", ", collection.Values.Select(v => $"'{v}'"))}). " +
                $"If the extra values are positional arguments, pass them after the '--' terminator " +
                $"(e.g. '{option.Name} {collection.Values[0]} -- {string.Join(" ", collection.Values.Skip(1))}')."),
            CliFlagOptionCapture => throw new CliOptionMaterializationException(
                $"The option '{option.Name}' cannot be used as a flag. Provide a single value instead."),
            _ => throw new CliOptionMaterializationException(
                $"The option '{option.Name}' is not in the expected format. Refer to the documentation for valid usage.")
        };
    }

    // Not a Try* method: unlike its siblings it never returns null. Scalar is the terminal case of
    // the CreateOrThrow cascade — an unacceptable declared type is a configuration error, so it
    // throws a type-specific CliConfigurationException rather than returning null (SOL-82).
    public static CliOptionMaterializer Create(
        CliOptionAttribute attribute,
        Type declaredType,
        bool isOptional,
        object? defaultValue)
    {
        if (!attribute.CanAccept(declaredType, out var typeConverter))
        {
            // The old message said "refer to the attribute's aliases to identify the problematic
            // configuration", which asked the user to do the framework's job. It knows the option and
            // it knows the type — so it says both, and what to do about it (POR-25).
            throw new CliConfigurationException(
                $"Option '{attribute.DisplayAliases}' has type '{declaredType.Name}', which cannot be built from a " +
                $"command-line string. Everything a user types is text, so an option's type needs a TypeConverter " +
                $"that converts from string. Give '{declaredType.Name}' a [TypeConverter], or use a type that " +
                $"already has one (a primitive, enum, string, TimeSpan, Guid, Uri, DateTime, or a collection of " +
                $"those). Analyzer POR010 reports this at compile time.");
        }


        object ParseValue(string value)
        {
            try
            {
                var result = typeConverter.ConvertFromInvariantString(value);
                if (result == null)
                {
                    throw new CliOptionMaterializationException(
                        attribute.Sensitive
                            ? $"Value for option '{attribute.DisplayAliases}' could not be converted to '{declaredType.Name}'."
                            : $"Value '{value}' for option '{attribute.DisplayAliases}' could not be converted to '{declaredType.Name}'.");
                }
                return result;
            }
            catch (CliOptionMaterializationException)
            {
                throw;
            }
            catch (FormatException ex) when (declaredType.IsEnum)
            {
                var expectedValueCsv = Enum
                    .GetNames(declaredType)
                    .Join(", ");
                throw new CliOptionMaterializationException(
                    attribute.Sensitive
                        ? $"Value for option '{attribute.DisplayAliases}' is invalid. Expected: {expectedValueCsv}."
                        : $"Value '{value}' for option '{attribute.DisplayAliases}' is invalid. Expected: {expectedValueCsv}.",
                    ex);
            }
            catch (Exception ex)
            {
                // A sensitive option echoes neither the value nor the converter's explanation — the
                // inner message embeds the value too ("'hunter2' is not a valid value for Int32").
                throw new CliOptionMaterializationException(
                    attribute.Sensitive
                        ? $"Value for option '{attribute.DisplayAliases}' is invalid."
                        : $"Value '{value}' for option '{attribute.DisplayAliases}' is invalid. {Explain(ex)}",
                    ex);
            }
        }

        return new CliScalarOptionMaterializer(attribute, ParseValue, isOptional, defaultValue);
    }
}

internal sealed class CliFlagOptionMaterializer : CliOptionMaterializer
{
    private readonly CliOptionAttribute _attribute;
    private readonly bool _isRequired;
    private readonly object _presentValue;   // value when the flag IS on the command line
    private readonly object? _absentValue;   // value when the flag is not (caller's declared default)

    private CliFlagOptionMaterializer(
        CliOptionAttribute attribute,
        bool isOptional,
        object presentValue,
        object? absentValue)
    {
        _attribute = attribute;
        _isRequired = !isOptional;
        _presentValue = presentValue;
        _absentValue = absentValue;
    }

    public override object? Materialize(CliInvocation invocation)
    {
        bool present = false;
        foreach (var option in invocation.Options)
        {
            if (!_attribute.IsMatch(option.Name)) continue;
            switch (option)
            {
                case CliFlagOptionCapture:
                    present = true;
                    break;
                case ICliMapOptionCapture:
                    throw new CliOptionMaterializationException(
                        $"The flag option '{option.Name}' cannot be used as a map.");
                default:
                    // A value-carrying capture on a flag is the positional-after-flag surprise
                    // (`run -v file.txt`): name the swallowed token and point at `--` (SOL-77).
                    var swallowed = FirstValueOf(option);
                    throw new CliOptionMaterializationException(
                        swallowed is null
                            ? $"The flag option '{option.Name}' cannot accept values."
                            : $"The flag option '{option.Name}' does not take a value, but received '{swallowed}'. " +
                              PositionalAfterOptionHint(swallowed));
            }
        }

        if (present) return _presentValue;

        // Absent from argv: the environment gets its say (POR-54). A flag has no value to convert, so
        // what the variable carries is a yes/no — see IsEnvironmentFlagSet for why `FOO=` is "no".
        if (EnvironmentValue(_attribute) is { } envValue && IsEnvironmentFlagSet(envValue))
        {
            return _presentValue;
        }

        if (_isRequired)
        {
            throw new CliOptionMaterializationException(
                $"The required flag option '{_attribute.DisplayAliases}' is missing.");
        }
        return _absentValue;
    }

    public static CliOptionMaterializer? TryCreateMaterializer(
        CliOptionAttribute attribute,
        Type declaredType,
        bool isOptional,
        object? absentValue)
    {
        if (CliFlag.IsFlagType(declaredType, out var presentValue))
        {
            return new CliFlagOptionMaterializer(attribute, isOptional, presentValue, absentValue);
        }

        return null;
    }

    /// <summary>The first bare token a flag wrongly swallowed, or <c>null</c> for value-less shapes.</summary>
    private static string? FirstValueOf(CliOptionCapture capture) => capture switch
    {
        CliScalarOptionCapture scalar => scalar.Value,
        CliCollectionOptionCapture collection => collection.Values.FirstOrDefault(),
        _ => null,
    };
}

/// <summary>
/// Materializes repeating or multi-value options into a collection. Supports 17 shapes (see
/// <see cref="SupportedCollectionDefinitions"/> plus <c>T[]</c>):
/// <list type="bullet">
///   <item><description>list-like — <c>T[]</c>, <c>List&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>,
///     <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>,
///     <c>IReadOnlyCollection&lt;T&gt;</c>, <c>ImmutableArray&lt;T&gt;</c>,
///     <c>ImmutableList&lt;T&gt;</c>, <c>IImmutableList&lt;T&gt;</c></description></item>
///   <item><description>set-like (dedup) — <c>HashSet&lt;T&gt;</c>, <c>SortedSet&lt;T&gt;</c>,
///     <c>ISet&lt;T&gt;</c>, <c>IReadOnlySet&lt;T&gt;</c>, <c>ImmutableHashSet&lt;T&gt;</c>,
///     <c>IImmutableSet&lt;T&gt;</c>, <c>ImmutableSortedSet&lt;T&gt;</c></description></item>
/// </list>
/// Both parsing styles work:
/// <list type="bullet">
///   <item><description><c>--files a b c</c> — one capture with N values</description></item>
///   <item><description><c>--file a --file b --file c</c> — N captures with 1 value each</description></item>
/// </list>
/// Strings are not treated as char collections even though <see cref="string"/> implements
/// <see cref="IEnumerable{T}"/>. Dictionaries are routed to
/// <see cref="CliDictionaryOptionMaterializer"/> instead.
/// </summary>
internal sealed class CliCollectionOptionMaterializer : CliOptionMaterializer
{
    private readonly CliOptionAttribute _attribute;
    private readonly Func<string, object> _itemParser;
    private readonly Func<object[], object> _collectionFactory;
    private readonly bool _isRequired;
    private readonly object? _defaultValue;

    private CliCollectionOptionMaterializer(
        CliOptionAttribute attribute,
        Func<string, object> itemParser,
        Func<object[], object> collectionFactory,
        bool isOptional,
        object? defaultValue)
    {
        _attribute = attribute;
        _itemParser = itemParser;
        _collectionFactory = collectionFactory;
        _isRequired = !isOptional;
        _defaultValue = defaultValue;
    }

    public override object? Materialize(CliInvocation invocation)
    {
        var captures = invocation.Options
            .Where(o => _attribute.IsMatch(o.Name))
            .ToList();

        if (captures.Count == 0)
        {
            // Absent from argv: the environment gets its say (POR-54). One variable has to carry
            // several values, and a comma is the convention every operator already knows
            // (PATH-style separators are platform-specific; whitespace collides with shell quoting).
            // A value that itself contains a comma cannot be expressed this way — argv remains the
            // escape hatch, and the docs say so rather than pretending otherwise.
            if (EnvironmentValue(_attribute) is { } envValue)
            {
                var envItems = envValue
                    .Split(',')
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .ToArray();

                if (envItems.Length > 0)
                {
                    return _collectionFactory(ParseItems(envItems));
                }
            }

            if (_isRequired)
            {
                throw new CliOptionMaterializationException(
                    $"The required option '{_attribute.DisplayAliases}' is missing. " +
                    $"Supply one or more values, e.g. --opt a b c.");
            }
            return _defaultValue;
        }

        var rawValues = new List<string>();
        foreach (var capture in captures)
        {
            switch (capture)
            {
                case CliFlagOptionCapture:
                    throw new CliOptionMaterializationException(
                        $"The collection option '{_attribute.DisplayAliases}' requires at least one value.");
                case ICliMapOptionCapture:
                    throw new CliOptionMaterializationException(
                        $"The collection option '{_attribute.DisplayAliases}' cannot accept a keyed map. Use a Dictionary<,> parameter for map semantics.");
                case CliScalarOptionCapture scalar:
                    rawValues.Add(scalar.Value);
                    break;
                case CliCollectionOptionCapture collection:
                    rawValues.AddRange(collection.Values);
                    break;
                default:
                    throw new CliOptionMaterializationException(
                        $"Unexpected capture for collection option '{_attribute.DisplayAliases}'.");
            }
        }

        return _collectionFactory(ParseItems(rawValues));
    }

    /// <summary>Converts each raw token to the element type. Shared by the argv and environment paths
    /// so a value from the environment is converted exactly as a typed one would be.</summary>
    private object[] ParseItems(IReadOnlyList<string> rawValues)
    {
        var parsed = new object[rawValues.Count];
        for (int i = 0; i < rawValues.Count; i++)
        {
            parsed[i] = _itemParser(rawValues[i]);
        }
        return parsed;
    }

    public static CliOptionMaterializer? TryCreateMaterializer(
        CliOptionAttribute attribute,
        Type declaredType,
        bool isOptional,
        object? defaultValue)
    {
        var itemType = GetCollectionItemType(declaredType);
        if (itemType is null) return null;

        if (!attribute.CanAccept(itemType, out var converter)) return null;

        var factory = BuildCollectionFactory(declaredType, itemType);
        if (factory is null) return null;

        object ParseItem(string raw)
        {
            try
            {
                var result = converter.ConvertFromInvariantString(raw);
                if (result is null)
                {
                    throw new CliOptionMaterializationException(
                        attribute.Sensitive
                            ? $"Value for option '{attribute.DisplayAliases}' could not be converted to '{itemType.Name}'."
                            : $"Value '{raw}' for option '{attribute.DisplayAliases}' could not be converted to '{itemType.Name}'.");
                }
                return result;
            }
            catch (CliOptionMaterializationException)
            {
                throw;
            }
            catch (FormatException ex) when (itemType.IsEnum)
            {
                var expected = string.Join(", ", Enum.GetNames(itemType));
                throw new CliOptionMaterializationException(
                    attribute.Sensitive
                        ? $"Value for option '{attribute.DisplayAliases}' is not a valid {itemType.Name}. Expected: {expected}."
                        : $"Value '{raw}' for option '{attribute.DisplayAliases}' is not a valid {itemType.Name}. Expected: {expected}.",
                    ex);
            }
            catch (Exception ex)
            {
                throw new CliOptionMaterializationException(
                    attribute.Sensitive
                        ? $"Value for option '{attribute.DisplayAliases}' is invalid."
                        : $"Value '{raw}' for option '{attribute.DisplayAliases}' is invalid. {Explain(ex)}",
                    ex);
            }
        }

        return new CliCollectionOptionMaterializer(attribute, ParseItem, factory, isOptional, defaultValue);
    }

    /// <summary>
    /// Recognized generic collection definitions. Includes list-like shapes (ordered, allows
    /// duplicates), set-like shapes (dedup), and their immutable counterparts. Dictionaries and
    /// map-like types handled separately earlier in the cascade.
    /// </summary>
    private static readonly HashSet<Type> SupportedCollectionDefinitions = new()
    {
        // List-like
        typeof(IEnumerable<>),
        typeof(IList<>),
        typeof(ICollection<>),
        typeof(IReadOnlyList<>),
        typeof(IReadOnlyCollection<>),
        typeof(List<>),
        typeof(ImmutableArray<>),
        typeof(ImmutableList<>),
        typeof(IImmutableList<>),
        // Set-like
        typeof(HashSet<>),
        typeof(SortedSet<>),
        typeof(ISet<>),
        typeof(IReadOnlySet<>),
        typeof(ImmutableHashSet<>),
        typeof(IImmutableSet<>),
        typeof(ImmutableSortedSet<>),
    };

    private static Type? GetCollectionItemType(Type declaredType)
    {
        // String is IEnumerable<char> structurally but a scalar semantically.
        if (declaredType == typeof(string)) return null;

        // Dictionaries handled separately, earlier in the cascade.
        if (declaredType.GetGenericDictionaryArgumentTypes().Any()) return null;

        if (declaredType.IsArray) return declaredType.GetElementType();

        if (declaredType.IsGenericType &&
            SupportedCollectionDefinitions.Contains(declaredType.GetGenericTypeDefinition()))
        {
            return declaredType.GetGenericArguments()[0];
        }

        // Fallback: a user type that implements IEnumerable<T> with a discoverable T.
        var fromInterfaces = declaredType.GetGenericEnumerableArgumentTypes().ToArray();
        return fromInterfaces.Length == 1 ? fromInterfaces[0] : null;
    }

    /// <summary>
    /// Produces a factory <c>object[] → object</c> that builds the declared collection type from
    /// the parsed item values. Three routing rules:
    /// <list type="number">
    ///   <item><description>Arrays: <c>T[]</c> — build via <see cref="Array.CreateInstance(Type,int)"/>.</description></item>
    ///   <item><description>List-like (anything <c>List&lt;T&gt;</c> can fill): build a <c>List&lt;T&gt;</c> and
    ///     return it typed as the declared parameter. Covers <c>IEnumerable</c>, <c>IList</c>,
    ///     <c>ICollection</c>, <c>IReadOnly*</c>, and <c>List</c> itself.</description></item>
    ///   <item><description>Set-like + immutable: dispatch to a per-shape factory backed by the
    ///     appropriate BCL constructor or <c>CreateRange</c> helper.</description></item>
    /// </list>
    /// </summary>
    private static Func<object[], object>? BuildCollectionFactory(Type declaredType, Type itemType)
    {
        if (declaredType.IsArray)
        {
            return items => CreateTypedArray(itemType, items);
        }

        // List-like: any declared type that a List<T> can fill.
        var listType = typeof(List<>).MakeGenericType(itemType);
        if (declaredType.IsAssignableFrom(listType))
        {
            return items =>
            {
                var list = (IList)Activator.CreateInstance(listType)!;
                foreach (var item in items) list.Add(item);
                return list;
            };
        }

        // Set-like and immutable shapes — dispatch by generic definition.
        if (!declaredType.IsGenericType) return null;
        var def = declaredType.GetGenericTypeDefinition();

        // HashSet / ISet / IReadOnlySet: HashSet<T>(IEnumerable<T>) constructor.
        if (def == typeof(HashSet<>) || def == typeof(ISet<>) || def == typeof(IReadOnlySet<>))
        {
            var hashSetType = typeof(HashSet<>).MakeGenericType(itemType);
            return items => Activator.CreateInstance(hashSetType, CreateTypedArray(itemType, items))!;
        }

        // SortedSet: SortedSet<T>(IEnumerable<T>) constructor.
        if (def == typeof(SortedSet<>))
        {
            var sortedSetType = typeof(SortedSet<>).MakeGenericType(itemType);
            return items => Activator.CreateInstance(sortedSetType, CreateTypedArray(itemType, items))!;
        }

        // ImmutableArray<T> (struct): ImmutableArray.CreateRange<T>(IEnumerable<T>).
        if (def == typeof(ImmutableArray<>))
        {
            var method = InvokeCreateRange(typeof(ImmutableArray), itemType);
            return items => method.Invoke(null, [CreateTypedArray(itemType, items)])!;
        }

        // ImmutableList / IImmutableList: ImmutableList.CreateRange<T>(IEnumerable<T>).
        if (def == typeof(ImmutableList<>) || def == typeof(IImmutableList<>))
        {
            var method = InvokeCreateRange(typeof(ImmutableList), itemType);
            return items => method.Invoke(null, [CreateTypedArray(itemType, items)])!;
        }

        // ImmutableHashSet / IImmutableSet: ImmutableHashSet.CreateRange<T>(IEnumerable<T>).
        if (def == typeof(ImmutableHashSet<>) || def == typeof(IImmutableSet<>))
        {
            var method = InvokeCreateRange(typeof(ImmutableHashSet), itemType);
            return items => method.Invoke(null, [CreateTypedArray(itemType, items)])!;
        }

        // ImmutableSortedSet: ImmutableSortedSet.CreateRange<T>(IEnumerable<T>).
        if (def == typeof(ImmutableSortedSet<>))
        {
            var method = InvokeCreateRange(typeof(ImmutableSortedSet), itemType);
            return items => method.Invoke(null, [CreateTypedArray(itemType, items)])!;
        }

        return null;
    }

    /// <summary>Builds a <c>T[]</c> from the parsed <c>object[]</c> items.</summary>
    private static Array CreateTypedArray(Type itemType, object[] items)
    {
        var arr = Array.CreateInstance(itemType, items.Length);
        for (int i = 0; i < items.Length; i++) arr.SetValue(items[i], i);
        return arr;
    }

    /// <summary>
    /// Locates and specializes the single-parameter <c>CreateRange&lt;T&gt;(IEnumerable&lt;T&gt;)</c>
    /// overload on a BCL factory type (<see cref="ImmutableArray"/>, <see cref="ImmutableList"/>,
    /// <see cref="ImmutableHashSet"/>, <see cref="ImmutableSortedSet"/>). Each factory has multiple
    /// overloads; we pick the one whose single parameter is <c>IEnumerable&lt;T&gt;</c>.
    /// </summary>
    private static MethodInfo InvokeCreateRange(Type factoryType, Type itemType)
    {
        foreach (var method in factoryType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.Name != "CreateRange" || !method.IsGenericMethodDefinition) continue;
            var ps = method.GetParameters();
            if (ps.Length != 1) continue;
            var pt = ps[0].ParameterType;
            if (pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return method.MakeGenericMethod(itemType);
            }
        }
        throw new InvalidOperationException(
            $"No single-parameter CreateRange<T>(IEnumerable<T>) overload found on {factoryType.FullName}. " +
            "Framework assumption broke; likely a BCL change.");
    }
}