using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;


namespace Portico.Reflection;

/// <summary>
/// Reflects a <c>[CliRoute]</c> method into an immutable <see cref="CliRouteModel"/> and acts as
/// the façade the application dispatches through. The route-model construction lives here; routing,
/// help rendering, and binding/dispatch are delegated to <see cref="CliRouteMatcher"/>,
/// <see cref="CliHelpRenderer"/>, and <see cref="CliMethodInvoker"/> respectively (SOL-78).
/// </summary>
internal sealed partial class CliMethodInfo : MethodInfoDecorator
{
    private readonly CliContext _context;
    private readonly ImmutableArray<ParameterInfoDecorator> _parameters;
    private readonly ImmutableArray<CliRouteSegment> _routeSegments;
    private readonly CliRouteModel _model;


    private CliMethodInfo(
        MethodInfo method,
        CliContext context,
        Type registeredType) : base(method)
    {
        _context = context;
        Debug.Assert(IsCliMethod(method));
        var parameters = base.GetParameters();
        var attributes = GetCustomAttributes(true).OfType<Attribute>().ToList();

        var rootSegments = BuildRootSegments(_context);
        var typePrefixSegments = BuildTypePrefixSegments(method, registeredType);
        var parameterLevelArgs = ResolveParameterLevelArguments(parameters);
        var resolvedRouteParts = ResolveRoutePlaceholders(parameters, attributes, parameterLevelArgs, out var placeholderArgs);

        // After placeholder resolution, no CliPlaceholderSegment instances remain — _routeSegments
        // is exclusively CliLiteralSegment + CliArgumentSegment. Every argument segment came from a
        // {placeholder} in a [CliRoute]: the route string is the command's path, in full.
        _routeSegments = [..rootSegments, ..typePrefixSegments, ..resolvedRouteParts];

        var argumentByParameter = MapArgumentsToParameters(parameters, placeholderArgs);

        _parameters = [.. BuildParameterInfos(parameters, argumentByParameter)];
        RejectDuplicateOptionAliases();

        var literalPrefix = _routeSegments
            .TakeWhile(s => s is CliLiteralSegment)
            .Cast<CliLiteralSegment>()
            .Select(s => s.Text)
            .ToImmutableArray();

        // Build the shared, immutable route model once. GetOptions() is materialized here rather
        // than re-yielded per consumer (SOL-78); the reflection walk above runs exactly once.
        _model = new CliRouteModel(
            name: Name,
            description: attributes.OfType<DescriptionAttribute>().Select(a => a.Description).FirstOrDefault(Name),
            segments: _routeSegments,
            parameters: _parameters,
            options: ComputeOptions().ToImmutableArray(),
            examples: [.. attributes.OfType<CliCommandExampleAttribute>()],
            literalPrefix: literalPrefix,
            mountPrefix: [.. rootSegments.OfType<CliLiteralSegment>().Select(s => s.Text)],
            routeSignature: ComputeRouteSignature(_routeSegments));
    }

    /// <summary>
    /// Verify that no option alias is declared twice across the method's direct
    /// <c>[CliOption]</c> parameters + bundle-parameter properties, <em>and</em> that none collides
    /// with a middleware-declared global option. Two options sharing an alias would both bind to the
    /// same capture at dispatch time, producing silently-shared state that almost never matches user
    /// intent — and a route option shadowing a global <c>-v</c> is the same hazard across the local /
    /// global boundary. Global-vs-global collisions are out of scope here (they are a per-application
    /// concern, not a per-method one).
    /// </summary>
    private void RejectDuplicateOptionAliases()
    {
        // CliAliasComparer, not StringComparer.Ordinal: this guard MUST agree with the matcher in
        // CliOptionSpec. When it used Ordinal and the matcher used OrdinalIgnoreCase, `-v` and `-V`
        // passed this check as distinct options and then both matched the same token at dispatch.
        var seen = new Dictionary<string, string>(CliAliasComparer.Instance);

        // Seed with the middleware-declared global options so a route option that reuses a global
        // alias is caught (POR-65). Seeded directly (no self-collision check) — two globals sharing an
        // alias is a per-application concern this per-method guard does not own.
        foreach (var global in _context.GlobalOptions)
        {
            if (global.Aliases.IsDefaultOrEmpty) continue;
            foreach (var alias in global.Aliases)
            {
                seen[alias] = $"global option '{global.Name}' (declared by middleware)";
            }
        }

        foreach (var parameter in _parameters)
        {
            if (parameter is CliOptionParameterInfo opt)
            {
                CheckAliases(opt.Aliases, $"parameter '{opt.Name}'", seen);
            }
            else if (parameter is CliOptionsParameterInfo bundle)
            {
                foreach (var bundleOption in bundle.GetOptions())
                {
                    CheckAliases(
                        bundleOption.Aliases,
                        $"property '{bundleOption.Name}' of bundle '{bundle.CliOptionsType.Name}'",
                        seen);
                }
            }
        }

        void CheckAliases(ImmutableArray<string> aliases, string origin, Dictionary<string, string> seenAliases)
        {
            if (aliases.IsDefaultOrEmpty) return;
            foreach (var alias in aliases)
            {
                if (seenAliases.TryGetValue(alias, out var existing))
                {
                    throw new CliConfigurationException(
                        $"Method '{DeclaringType?.FullName}.{Name}': option alias '{alias}' is declared by " +
                        $"both {existing} and {origin}. Each alias must be unique per command — two " +
                        $"parameters (or bundle properties) binding the same option would silently receive " +
                        $"the same captured value at dispatch time.");
                }
                seenAliases[alias] = origin;
            }
        }
    }

    private static IReadOnlyList<CliRouteSegment> BuildRootSegments(CliContext context) =>
        context.RootRoutes
            .SelectMany(r => Regex.Split(r.RouteSignature, @"\s+"))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Select(s => (CliRouteSegment)new CliLiteralSegment(s))
            .ToList();

    /// <summary>
    /// Resolve the type-level <c>[CliRoute]</c> prefix applied to a method. Registered class wins
    /// over declaring interface; if neither carries one, returns an empty list.
    /// </summary>
    private static IReadOnlyList<CliRouteSegment> BuildTypePrefixSegments(MethodInfo method, Type registeredType)
    {
        var classAttr = registeredType.GetCustomAttribute<CliRouteAttribute>(inherit: false);
        var declaringAttr = method.DeclaringType != registeredType
            ? method.DeclaringType?.GetCustomAttribute<CliRouteAttribute>(inherit: false)
            : null;
        var prefix = classAttr ?? declaringAttr;
        if (prefix is null) return Array.Empty<CliRouteSegment>();

        return Regex.Split(prefix.RouteSignature, @"\s+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Select(s => (CliRouteSegment)new CliLiteralSegment(s))
            .ToList();
    }

    /// <summary>
    /// Discover <c>[CliArgument(description)]</c> attributes on the method's parameters and resolve
    /// each to its parameter's name via reflection. These describe arguments; they never declare
    /// route position — <see cref="ResolveRoutePlaceholders"/> matches each to a <c>{placeholder}</c>
    /// and rejects any that matches none.
    /// </summary>
    private List<CliArgumentAttribute> ResolveParameterLevelArguments(ParameterInfo[] parameters)
    {
        var result = new List<CliArgumentAttribute>();
        foreach (var parameter in parameters)
        {
            var paramAttrs = parameter
                .GetCustomAttributes(typeof(CliArgumentAttribute), true)
                .OfType<CliArgumentAttribute>()
                .ToArray();
            if (paramAttrs.Length == 0) continue;
            if (paramAttrs.Length > 1)
            {
                throw new CliConfigurationException(
                    $"Parameter '{parameter.Name}' on '{DeclaringType?.FullName}.{Name}' declares " +
                    $"{paramAttrs.Length} [CliArgument] attributes. At most one is allowed per parameter.");
            }

            var attr = paramAttrs[0];
            var resolvedName = parameter.Name ?? string.Empty;

            // ParameterName / Name are open for internal writes so the ctor can be name-less and
            // still plumb through the routing code. Name is only defaulted, never overwritten — an
            // author who wrote [CliArgument("desc", Name = "PATH")] chose that display form.
            attr.ParameterName = resolvedName;
            if (string.IsNullOrWhiteSpace(attr.Name)) attr.Name = resolvedName;
            result.Add(attr);
        }
        return result;
    }

    /// <summary>
    /// Walk <c>{name}</c> placeholders in the route signature and replace each with a
    /// <see cref="CliArgumentSegment"/> bound to the matching parameter. Throws at Create time when a
    /// placeholder matches no parameter, or when a <c>[CliArgument]</c> matches no placeholder.
    /// </summary>
    /// <remarks>
    /// The route string is the command's path in full — every argument segment originates here, from
    /// a <c>{placeholder}</c>, never from an attribute's position or a parameter's ordinal. A
    /// <c>[CliArgument]</c> on a placeholder-bound parameter supplies the description and display
    /// name and is consumed into the segment; one that matches no placeholder is a configuration
    /// error. The Roslyn analyzers promote both checks to compile time (POR001, POR005); these
    /// runtime checks stay as the defense for users who build without the analyzers installed.
    /// </remarks>
    private List<CliRouteSegment> ResolveRoutePlaceholders(
        ParameterInfo[] parameters,
        IReadOnlyList<Attribute> attributes,
        List<CliArgumentAttribute> parameterLevelArgs,
        out List<CliArgumentAttribute> placeholderArgs)
    {
        placeholderArgs = new List<CliArgumentAttribute>();
        var parts = ExtractRouteParts(attributes).ToList();
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] is not CliPlaceholderSegment placeholder) continue;

            if (!parameters.Any(p => string.Equals(p.Name, placeholder.Name, StringComparison.Ordinal)))
            {
                var parameterList = parameters.Length == 0
                    ? "(method takes no parameters)"
                    : string.Join(", ", parameters.Select(p => p.Name));
                throw new CliConfigurationException(
                    $"Method '{DeclaringType?.FullName}.{Name}' declares route placeholder " +
                    $"'{{{placeholder.Name}}}' but no parameter '{placeholder.Name}' exists on the method. " +
                    $"Available parameters: {parameterList}. " +
                    $"Either rename the placeholder, or add the parameter.");
            }

            // Reuse the parameter's own [CliArgument] when it has one — that instance already carries
            // the author's description and any Name override. Synthesize only when it has none.
            var declared = parameterLevelArgs.FirstOrDefault(a => a.ParameterName == placeholder.Name);
            if (declared is not null)
            {
                parameterLevelArgs.Remove(declared);
            }

            var argument = declared ?? new CliArgumentAttribute(placeholder.Name)
            {
                ParameterName = placeholder.Name,
                Name = placeholder.Name
            };

            placeholderArgs.Add(argument);
            parts[i] = new CliArgumentSegment(argument);
        }

        RejectArgumentsWithoutAPlaceholder(parameterLevelArgs, attributes);
        return parts;
    }

    /// <summary>
    /// Every <c>[CliArgument]</c> left unconsumed describes a parameter the route declares no
    /// <c>{placeholder}</c> for. Before POR-70 such an attribute silently appended a segment to the
    /// route, so a command's path depended on C# parameter order and was invisible in its route
    /// string. It is now a configuration error, and the message carries the corrected route.
    /// </summary>
    private void RejectArgumentsWithoutAPlaceholder(
        List<CliArgumentAttribute> unconsumed,
        IReadOnlyList<Attribute> attributes)
    {
        if (unconsumed.Count == 0) return;

        var route = attributes.OfType<CliRouteAttribute>().FirstOrDefault()?.RouteSignature ?? string.Empty;
        var names = unconsumed.Select(a => a.ParameterName).ToList();
        var corrected = $"{route} {names.Select(n => $"{{{n}}}").Join(" ")}".Trim();

        var subject = names.Count == 1
            ? $"parameter '{names[0]}' carries [CliArgument] but the route \"{route}\" " +
              $"declares no {{{names[0]}}} placeholder"
            : $"parameters {names.Select(n => $"'{n}'").Join(", ")} carry [CliArgument] but the " +
              $"route \"{route}\" declares no {names.Select(n => $"{{{n}}}").Join(", ")} placeholders";

        throw new CliConfigurationException(
            $"Method '{DeclaringType?.FullName}.{Name}': {subject}. " +
            $"A command's path is declared entirely by [CliRoute] — put the argument in the route: " +
            $"[CliRoute(\"{corrected}\")]. [CliArgument] only describes an argument the route already declares.");
    }

    /// <summary>
    /// Build the lookup from <see cref="ParameterInfo"/> to its argument metadata
    /// (attribute + position in <see cref="_routeSegments"/>). Parameters not bound to an
    /// argument are absent from the map; the caller treats them as options or bundles.
    /// </summary>
    private Dictionary<ParameterInfo, (CliArgumentAttribute Attribute, int Position)> MapArgumentsToParameters(
        ParameterInfo[] parameters,
        IReadOnlyList<CliArgumentAttribute> placeholderArgs)
    {
        var positionByAttribute = new Dictionary<CliArgumentAttribute, int>();
        for (int i = 0; i < _routeSegments.Length; i++)
        {
            if (_routeSegments[i] is CliArgumentSegment arg)
            {
                positionByAttribute[arg.Argument] = i;
            }
        }

        var result = new Dictionary<ParameterInfo, (CliArgumentAttribute, int)>();
        foreach (var attribute in placeholderArgs)
        {
            var parameter = parameters.FirstOrDefault(attribute.References);
            if (parameter is null) continue;
            result[parameter] = (attribute, positionByAttribute[attribute]);
        }

        return result;
    }

    /// <summary>
    /// Build the per-parameter <see cref="ParameterInfoDecorator"/> hierarchy: argument,
    /// option-bundle, ambient cancellation token, or scalar option — in that decision order.
    /// </summary>
    private static List<ParameterInfoDecorator> BuildParameterInfos(
        ParameterInfo[] parameters,
        Dictionary<ParameterInfo, (CliArgumentAttribute Attribute, int Position)> argumentByParameter)
    {
        var result = new List<ParameterInfoDecorator>(parameters.Length);
        foreach (var parameter in parameters)
        {
            if (argumentByParameter.TryGetValue(parameter, out var arg))
            {
                if (parameter.GetCustomAttributes(typeof(CliOptionAttribute), true).Any())
                {
                    throw new CliConfigurationException(
                        $"Parameter '{parameter.Name}' on method '{parameter.Member.DeclaringType?.FullName}.{parameter.Member.Name}' " +
                        $"is bound to a route argument (via [CliRoute] placeholder or [CliArgument]) and also carries a [CliOption] " +
                        $"attribute. A parameter is either a positional argument or a named option — pick one. " +
                        $"Remove the [CliOption] to keep it as an argument, or rename it (and drop the placeholder / [CliArgument]) to keep it as an option.");
                }
                result.Add(new CliArgumentParameterInfo(parameter, arg.Attribute, arg.Position));
            }
            else if (CliOptions.IsAssignableFrom(parameter.ParameterType))
            {
                result.Add(new CliOptionsParameterInfo(parameter));
            }
            else if (parameter.ParameterType == typeof(CancellationToken) &&
                     !parameter.GetCustomAttributes(typeof(CliOptionAttribute), true).Any())
            {
                result.Add(new CliCancellationTokenParameterInfo(parameter));
            }
            else
            {
                result.Add(new CliOptionParameterInfo(parameter));
            }
        }
        return result;
    }

    public static CliMethodInfo[] Get(Type type, CliContext context)
    {
        var methods = type
            .GetInterfaces()
            .Prepend(type)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Distinct()
            .ToList();

        var list = new List<CliMethodInfo>();

        foreach (var method in methods)
        {
            if (false == IsCliMethod(method))
            {
                continue;
            }

            list.Add(new CliMethodInfo(method, context, type));
        }

        return list.ToArray();
    }

    // -----------------------------------------------------------------------------------------
    //  Route-model accessors (the model is the single source of truth; these are thin readers)
    // -----------------------------------------------------------------------------------------

    public string Description => _model.Description;

    public new ImmutableArray<ParameterInfoDecorator> GetParameters() => _model.Parameters;

    public ImmutableArray<CliCommandExampleAttribute> Examples => _model.Examples;

    public IEnumerable<ICliOptionMemberInfo> GetOptions() => _model.Options;

    /// <summary>The route model: the single source of truth the help renderer reads.</summary>
    public CliRouteModel RouteModel => _model;

    internal IEnumerable<CliRouteSegment> Segments => _model.Segments;

    /// <summary>
    /// Literal prefix of the route (i.e. all <see cref="CliLiteralSegment"/>s up to the first
    /// <see cref="CliArgumentSegment"/>, in declaration order). Used by the application's
    /// help-path matching (<c>myapp init --help</c> with a required <c>{path}</c> argument)
    /// and by fuzzy-suggestion ranking — both need to compare what the user typed against the
    /// literal part of the route, without synthesizing argument placeholders.
    /// </summary>
    public ImmutableArray<string> LiteralPrefix => _model.LiteralPrefix;

    /// <summary>
    /// Canonical route signature used for duplicate detection and help/suggestion rendering.
    /// Literal segments appear verbatim; argument slots appear as <c>{argName}</c>.
    /// </summary>
    public string RouteSignature => _model.RouteSignature;

    // -----------------------------------------------------------------------------------------
    //  Dispatch / routing / help — delegated to the collaborators
    // -----------------------------------------------------------------------------------------

    [DebuggerStepThrough]
    public int Invoke(object? instance, CliInvocation invocation) =>
        InvokeAsync(instance, invocation, CancellationToken.None).GetAwaiter().GetResult();

    public Task<int> InvokeAsync(object? instance, CliInvocation invocation, CancellationToken cancellationToken) =>
        CliMethodInvoker.InvokeAsync(_model, _context, InvokeUnderlying, instance, invocation, cancellationToken);

    // `base` access isn't allowed inside a lambda, so expose the underlying reflection call as a
    // method group the invoker can hold as a delegate.
    private object? InvokeUnderlying(object? instance, object?[] args) => base.Invoke(instance, args);

    public bool IsMatch(CliInvocation invocation) => CliRouteMatcher.IsMatch(_model, invocation);

    public double RankByOptions(CliInvocation invocation) => CliRouteMatcher.RankByOptions(_model, invocation);

    public string ToCommandHelpString(string executableName) => CliHelpRenderer.RenderCommandHelp(_model, executableName);

    // -----------------------------------------------------------------------------------------
    //  Construction helpers for the route model
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Materializes every option this route can bind — direct <c>[CliOption]</c> parameters,
    /// bundle-property options, then the application's global options. Called once from the ctor;
    /// the result is cached on <see cref="CliRouteModel.Options"/>.
    /// </summary>
    private IEnumerable<ICliOptionMemberInfo> ComputeOptions()
    {
        foreach (var parameter in _parameters)
        {
            if (parameter is CliOptionParameterInfo optionParameter)
            {
                yield return optionParameter;
            }
            else if (parameter is CliOptionsParameterInfo optionBundleParameter)
            {
                foreach (var o in optionBundleParameter.GetOptions())
                {
                    yield return o;
                }
            }
        }

        foreach (var option in _context.GlobalOptions)
        {
            yield return option;
        }
    }

    private static string ComputeRouteSignature(ImmutableArray<CliRouteSegment> segments) =>
        segments
            .Select(segment => segment switch
            {
                CliLiteralSegment literal => literal.Text,
                CliArgumentSegment arg => $"{{{arg.Argument.Name}}}",
                _ => "?"
            })
            .Join(" ");

    private static bool IsCliMethod(MethodInfo methodInfo)
    {
        return methodInfo.GetCustomAttributes<CliRouteAttribute>().Any();
    }

    [GeneratedRegex(@"^\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}$", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    /// <summary>
    /// Tokenizes the method's <see cref="CliRouteAttribute"/> into a sequence of
    /// <see cref="CliRouteSegment"/>s: literal tokens become <see cref="CliLiteralSegment"/> and
    /// <c>{name}</c> placeholders become <see cref="CliPlaceholderSegment"/>, resolved later by
    /// <see cref="ResolveRoutePlaceholders"/>. Only the route string contributes segments — no other
    /// attribute does, so the order attributes happen to be declared in cannot move a path segment.
    /// </summary>
    private static IReadOnlyList<CliRouteSegment> ExtractRouteParts(IReadOnlyList<Attribute> attributes)
    {
        return attributes
            .SelectMany<Attribute, CliRouteSegment>(attribute =>
            {
                if (attribute is CliRouteAttribute route)
                {
                    return Regex
                        .Split(route.RouteSignature, @"\s+")
                        .Where(r => !string.IsNullOrWhiteSpace(r))
                        .Select(r => r.Trim())
                        .Select<string, CliRouteSegment>(r =>
                        {
                            var ph = PlaceholderRegex().Match(r);
                            return ph.Success
                                ? new CliPlaceholderSegment(ph.Groups["name"].Value)
                                : new CliLiteralSegment(r);
                        });
                }

                return [];
            })
            .ToList();
    }

    public override string ToString()
    {
        var builder = new StringBuilder(Name);
        var parameters =
            GetParameters()
                .Select(p =>
                {
                    if (p is CliArgumentParameterInfo)
                    {
                        return $"arg: {p.Name}";
                    }

                    if (p is CliOptionsParameterInfo)
                    {
                        return $"bundle: {p.Name}";
                    }

                    return p.Name;
                })!
                .Join(", ");
        builder.Append($"({parameters})");
        return builder.ToString();
    }
}
