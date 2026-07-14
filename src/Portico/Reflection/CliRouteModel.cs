using System.Collections.Immutable;
using System.Linq;

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
        ImmutableArray<string> mountPrefix,
        string routeSignature)
    {
        Name = name;
        Description = description;
        Segments = segments;
        Parameters = parameters;
        Options = options;
        Examples = examples;
        LiteralPrefix = literalPrefix;
        MountPrefix = mountPrefix;
        RouteSignature = routeSignature;
        MinSegmentCount = ComputeMinSegmentCount(segments, parameters);
        RejectNonTrailingOptionalArguments(name, parameters, MinSegmentCount);
    }

    /// <summary>
    /// How many positional segments the user must supply. Below <see cref="Segments"/>.Length only
    /// when the route ends in a run of optional arguments (a <c>[CliArgument]</c> parameter with a
    /// C# default), each of which may be omitted from the right.
    /// </summary>
    public int MinSegmentCount { get; }

    /// <summary>
    /// Walks the segments from the right, shortening the required prefix for each trailing optional
    /// argument. A literal or a required argument stops the walk — which is what confines
    /// optionality to the tail and keeps matching unambiguous.
    /// </summary>
    private static int ComputeMinSegmentCount(
        ImmutableArray<CliRouteSegment> segments,
        ImmutableArray<ParameterInfoDecorator> parameters)
    {
        var min = segments.Length;
        for (var i = segments.Length - 1; i >= 0; --i)
        {
            if (segments[i] is not CliArgumentSegment) break;

            var bound = parameters
                .OfType<CliArgumentParameterInfo>()
                .FirstOrDefault(p => p.CliRoutePosition == i);

            if (bound is null || !bound.IsOptionalArgument) break;

            min = i;
        }

        return min;
    }

    /// <summary>
    /// An optional argument with a required one after it is unresolvable: given fewer tokens than
    /// slots, the framework cannot know which slot the user meant to skip. Reject it at
    /// <c>CliApplication.Create</c> rather than binding something arbitrary at dispatch.
    /// </summary>
    private static void RejectNonTrailingOptionalArguments(
        string routeName,
        ImmutableArray<ParameterInfoDecorator> parameters,
        int minSegmentCount)
    {
        foreach (var argument in parameters.OfType<CliArgumentParameterInfo>())
        {
            if (argument.IsOptionalArgument && argument.CliRoutePosition < minSegmentCount)
            {
                throw new CliConfigurationException(
                    $"Route '{routeName}': the optional argument <{argument.CliArgumentName.ToUpperInvariant()}> " +
                    $"is followed by a required positional. An optional argument must be last — otherwise, given " +
                    $"fewer tokens than slots, the framework cannot tell which one you meant to omit. Give the " +
                    $"later arguments defaults too, or drop the default from this one.");
            }
        }
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

    /// <summary>
    /// The root-route segments this command was mounted under (<c>AddCommands(instance, rootRoutes)</c>),
    /// empty when it was registered at the root. A <c>[CliCommandExample]</c> is authored against the
    /// contract, which cannot know its mount — so help must prepend this prefix to keep the example
    /// copy-pasteable (POR-39). The type-level <c>[CliRoute]</c> prefix is NOT part of it: that one is
    /// visible to the example's author and is expected to be spelled out in the example itself.
    /// </summary>
    public ImmutableArray<string> MountPrefix { get; }

    /// <summary>Canonical signature: literals verbatim, argument slots as <c>{argName}</c>.</summary>
    public string RouteSignature { get; }
}
