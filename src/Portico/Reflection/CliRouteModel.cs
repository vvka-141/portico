using System.Collections.Immutable;

namespace Portico.Reflection;

/// <summary>
/// The immutable route model produced by <see cref="CliMethodInfo"/> during construction and
/// shared, read-only, by the three collaborators that used to live inside the same 970-line type
/// (SOL-78): <see cref="CliRouteMatcher"/> (routing / ranking), <see cref="CliHelpRenderer"/>
/// (help output), and <see cref="CliMethodInvoker"/> (binding / dispatch). Splitting the model
/// out keeps each collaborator a pure function of this record — the reflection walk that builds it
/// runs exactly once.
/// </summary>
internal sealed class CliRouteModel
{
    public CliRouteModel(
        string name,
        string description,
        ImmutableArray<CliRouteSegment> segments,
        ImmutableArray<ParameterInfoDecorator> parameters,
        ImmutableArray<ICliOptionMemberInfo> options,
        ImmutableArray<CliCommandExampleAttribute> examples,
        ImmutableArray<string> literalPrefix,
        string routeSignature)
    {
        Name = name;
        Description = description;
        Segments = segments;
        Parameters = parameters;
        Options = options;
        Examples = examples;
        LiteralPrefix = literalPrefix;
        RouteSignature = routeSignature;
    }

    /// <summary>The reflected method name — used in dispatch error messages and help.</summary>
    public string Name { get; }

    /// <summary>Route description (the <c>[Description]</c> attribute, or the method name).</summary>
    public string Description { get; }

    /// <summary>
    /// The resolved route: exclusively <see cref="CliLiteralSegment"/> + <see cref="CliArgumentSegment"/>
    /// (no placeholders remain after construction).
    /// </summary>
    public ImmutableArray<CliRouteSegment> Segments { get; }

    /// <summary>The bound method parameters (arguments, options, bundles, cancellation token).</summary>
    public ImmutableArray<ParameterInfoDecorator> Parameters { get; }

    /// <summary>
    /// Every option this route can bind — direct <c>[CliOption]</c> parameters, bundle-property
    /// options, and the application's global options. Computed once (SOL-78); previously each
    /// consumer re-materialized the <c>yield</c> via <c>GetOptions().ToList()</c>.
    /// </summary>
    public ImmutableArray<ICliOptionMemberInfo> Options { get; }

    /// <summary>The <c>[CliCommandExample]</c> attributes, in declaration order.</summary>
    public ImmutableArray<CliCommandExampleAttribute> Examples { get; }

    /// <summary>Literal segments up to the first argument slot (help-path matching + suggestions).</summary>
    public ImmutableArray<string> LiteralPrefix { get; }

    /// <summary>Canonical signature: literals verbatim, argument slots as <c>{argName}</c>.</summary>
    public string RouteSignature { get; }
}
