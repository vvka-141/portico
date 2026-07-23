using System;
using System.ComponentModel;
using System.Reflection;
using System.Threading;

namespace Portico;

/// <summary>
/// Describes a positional CLI argument. Applied to the method parameter a
/// <see cref="CliRouteAttribute"/> placeholder already binds — it supplies the help text and, via
/// <see cref="Name"/>, the display form; it never adds a segment to the route.
/// </summary>
/// <remarks>
/// <b>The route string is the command's path, in full.</b> A command's shape is declared entirely by
/// <c>[CliRoute]</c>, exactly as an ASP.NET Core route template declares <c>{id}</c> inline — this
/// attribute is the CLI's <c>[FromRoute]</c>, not a way to append a segment. A
/// <c>[CliArgument]</c> on a parameter the route declares no <c>{placeholder}</c> for is a
/// configuration error, reported at <see cref="CliApplication.Create"/> and by analyzer rule POR005.
/// </remarks>
/// <example><code>
/// [CliRoute("user {id} details")]
/// [CliCommandExample("user 42 details", "show one user")]
/// public int Details([CliArgument("which user")] string id) =&gt; 0;
/// </code></example>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true)]
public class CliArgumentAttribute : Attribute
{
    /// <summary>
    /// Describes the argument bound to the parameter this attribute decorates. The parameter name is
    /// resolved by reflection, so the description is all you supply:
    /// <c>[CliArgument("target path")] string path</c>.
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
    /// Gets the name of the parameter this attribute describes. Empty immediately after
    /// construction — the reflection pipeline fills it in during route discovery. Use
    /// <see cref="Name"/> for the resolved display name.
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
    /// Defaults to the reflected parameter name. Set it via a named attribute argument to override
    /// the display form: <c>[CliArgument("target path", Name = "PATH")] string path</c>.
    /// </remarks>
    public string Name { get; set; }

    /// <summary>
    /// Checks if this attribute describes a specific parameter.
    /// </summary>
    /// <param name="pi">The ParameterInfo to check against.</param>
    /// <returns>True if this attribute's parameter name matches the provided ParameterInfo's name; otherwise, false.</returns>
    /// <example><code>
    /// var argument = new CliArgumentAttribute("target path") { ParameterName = "path" };
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
    /// var argument = new CliArgumentAttribute("target path") { ParameterName = "path" };
    /// string name = argument.ToString(); // "path"
    /// </code></example>
    public override string ToString() => ParameterName;

}