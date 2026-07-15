using System;
using System.ComponentModel;
using System.Reflection;
using System.Threading;

namespace Portico;

/// <summary>
/// Declares a method parameter as a positional CLI argument. Two placement styles:
/// <list type="bullet">
///   <item><description><b>Method-level</b> — <c>[CliArgument(nameof(path), "desc")]</c> on the
///     method, referencing a parameter by name. Required when arguments must appear in the middle
///     of the route signature (interleaved with <see cref="CliRouteAttribute"/>).</description></item>
///   <item><description><b>Parameter-level</b> — <c>[CliArgument("desc")]</c> on the parameter
///     itself; the framework resolves the parameter name from reflection. Arguments declared this
///     way append to the end of the route signature in parameter order.</description></item>
/// </list>
/// Both styles can coexist on the same method, but a given parameter is declared in exactly one
/// place — the framework rejects duplicates at configuration time.
/// </summary>
/// <example><code>
/// [CliRoute("greet")]
/// [CliCommandExample("greet hello", "run the greet subcommand")]
/// public int Greet([CliArgument("the subcommand")] string verb) =&gt; 0;
/// </code></example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = true)]
public class CliArgumentAttribute : Attribute
{
    /// <summary>
    /// Method-level form: reference a parameter by name, supply a description.
    /// </summary>
    public CliArgumentAttribute(
        string parameterName,
        string description)
    {
        ParameterName = ThrowIf
            .ArgumentNullOrWhiteSpace(parameterName)
            .Trim();
        Description = description
            .DefaultIfNullOrWhiteSpace(ParameterName);
        Name = ParameterName;
    }

    /// <summary>
    /// Parameter-level form: the parameter name is resolved from reflection. Use this on the
    /// parameter itself, e.g. <c>[CliArgument("target path")] string path</c>.
    /// </summary>
    public CliArgumentAttribute(string description = "")
    {
        // Empty marker — the reflection pipeline replaces this with the actual parameter name
        // during CliMethodInfo construction. Consumers should read Name, not ParameterName,
        // once resolution has happened.
        ParameterName = string.Empty;
        Description = description ?? string.Empty;
        Name = string.Empty;
    }

    /// <summary>
    /// Gets the name of the parameter this attribute references. Empty immediately after a
    /// parameter-level construction — the reflection pipeline fills it in during route
    /// discovery. Use <see cref="Name"/> for the resolved display name.
    /// </summary>
    public string ParameterName { get; set; }

    /// <summary>
    /// Gets a description of the parameter, used to generate help text in CLI applications.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets or sets the display name shown for this argument in help output (e.g. <c>&lt;PATH&gt;</c>).
    /// </summary>
    /// <remarks>
    /// Display name used in help output (e.g. <c>&lt;PATH&gt;</c>). For method-level placement
    /// this is the supplied <see cref="ParameterName"/>; for parameter-level placement this is
    /// the reflected parameter name. May be set via named attribute argument to override the
    /// display form (<c>[CliArgument(nameof(p), "desc", Name = "PATH")]</c>).
    /// </remarks>
    public string Name { get; set; }

    /// <summary>
    /// Determines if this attribute conflicts with another by comparing their parameter names.
    /// </summary>
    /// <param name="other">Another CliArgumentAttribute to compare against.</param>
    /// <returns>True if both attributes refer to the same parameter name; otherwise, false.</returns>
    /// <example><code>
    /// var a = new CliArgumentAttribute("path", "target path");
    /// var b = new CliArgumentAttribute("path", "another");
    /// bool clash = a.Conflicts(b); // true
    /// </code></example>
    public bool Conflicts(CliArgumentAttribute other) => ParameterName
        .Equals(other.ParameterName, StringComparison.Ordinal);


    /// <summary>
    /// Checks if this attribute references a specific parameter.
    /// </summary>
    /// <param name="pi">The ParameterInfo to check against.</param>
    /// <returns>True if this attribute's parameter name matches the provided ParameterInfo's name; otherwise, false.</returns>
    /// <example><code>
    /// var argument = new CliArgumentAttribute("path", "target path");
    /// bool refersTo = argument.References(parameterInfo); // true when parameterInfo.Name == "path"
    /// </code></example>
    public bool References(ParameterInfo pi) => ParameterName.Equals(pi.Name);

    /// <summary>
    /// Tests whether a CLI string can be bound to <paramref name="argumentType"/>, returning
    /// the <see cref="TypeConverter"/> the framework will use at materialization time. Recognizes
    /// <see cref="TimeSpan"/> and <see cref="CancellationToken"/> specially (multi-format duration
    /// strings; <c>--timeout</c>-style cancellation); falls back to the type's discovered
    /// <see cref="TypeDescriptor.GetConverter(Type)"/> for everything else. Override on a derived
    /// attribute to widen acceptance for a custom argument type.
    /// </summary>
    /// <param name="argumentType">The target parameter type. Nullable wrappers are unwrapped first.</param>
    /// <param name="converter">The converter to use, or the discovered default if unsupported.</param>
    /// <returns><see langword="true"/> if the converter advertises CLI-operand conversion support.</returns>
    /// <example><code>
    /// var argument = new CliArgumentAttribute("the timeout");
    /// if (argument.CanAccept(typeof(TimeSpan), out var converter)) { /* duration values bind */ }
    /// </code></example>
    public virtual bool CanAccept(Type argumentType, out TypeConverter converter)
    {
        argumentType = Nullable.GetUnderlyingType(argumentType) ?? argumentType;
        converter = TypeDescriptor.GetConverter(argumentType);

        if (argumentType == typeof(TimeSpan))
        {
            converter = new MultiFormatTimeSpanConverter();
            ThrowIf.False(converter.CanConvertFrom(typeof(string)));
        }
        else if (argumentType == typeof(CancellationToken))
        {
            converter = new CliCancellationTokenTypeConverter();
            ThrowIf.False(converter.CanConvertFrom(typeof(string)));
        }
        return converter.SupportsCliOperandConversion();
    }

    /// <summary>
    /// Returns a string that represents the parameter name.
    /// </summary>
    /// <returns>A string representation of the parameter name.</returns>
    /// <example><code>
    /// var argument = new CliArgumentAttribute("path", "target path");
    /// string name = argument.ToString(); // "path"
    /// </code></example>
    public override string ToString() => ParameterName;

}