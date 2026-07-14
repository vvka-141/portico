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
internal sealed partial class CliMethodInfo : MethodInfoDecorator, IFormattable
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

        RejectMultipleRouteAttributes(attributes);

        var rootSegments = BuildRootSegments(_context);
        var typePrefixSegments = BuildTypePrefixSegments(method, registeredType);
        var parameterLevelArgs = ResolveParameterLevelArguments(parameters, attributes);
        var resolvedRouteParts = ResolveRoutePlaceholders(parameters, attributes, parameterLevelArgs, out var placeholderArgs);

        // After placeholder resolution, no CliPlaceholderSegment instances remain — _routeSegments
        // is exclusively CliLiteralSegment + CliArgumentSegment.
        _routeSegments = [
            ..rootSegments,
            ..typePrefixSegments,
            ..resolvedRouteParts,
            ..parameterLevelArgs.Select(a => (CliRouteSegment)new CliArgumentSegment(a))
        ];

        var argumentByParameter = MapArgumentsToParameters(parameters, attributes, placeholderArgs, parameterLevelArgs);

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
            routeSignature: ComputeRouteSignature(_routeSegments));
    }

    /// <summary>
    /// Verify that no option alias is declared twice across the method's direct
    /// <c>[CliOption]</c> parameters + bundle-parameter properties. Two parameters with the
    /// same alias would both bind to the same capture at dispatch time, producing
    /// silently-shared state that almost never matches user intent. Middleware aliases live
    /// in a separate global pool and are checked per-application, not per-method.
    /// </summary>
    private void RejectDuplicateOptionAliases()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
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

    private void RejectMultipleRouteAttributes(IReadOnlyList<Attribute> attributes)
    {
        var routeAttributes = attributes.OfType<CliRouteAttribute>().ToList();
        if (routeAttributes.Count <= 1) return;
        throw new CliConfigurationException(
            $"Method '{DeclaringType?.FullName}.{Name}' declares {routeAttributes.Count} [CliRoute] attributes. " +
            "Only one [CliRoute] per method is supported — multiple attributes previously flattened into a single " +
            "concatenated route and did not behave as aliases. If you need route aliases, pick one canonical route " +
            "for now; first-class alias syntax is on the roadmap.");
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
    /// Discover parameter-level <c>[CliArgument(description)]</c> attributes, resolve each to its
    /// parameter's name via reflection, and reject conflicts with method-level
    /// <c>[CliArgument(nameof(x), …)]</c> for the same parameter.
    /// </summary>
    private List<CliArgumentAttribute> ResolveParameterLevelArguments(
        ParameterInfo[] parameters,
        IReadOnlyList<Attribute> attributes)
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

            var conflicting = attributes
                .OfType<CliArgumentAttribute>()
                .FirstOrDefault(a => a.ParameterName == resolvedName);
            if (conflicting is not null)
            {
                throw new CliConfigurationException(
                    $"Parameter '{resolvedName}' on '{DeclaringType?.FullName}.{Name}' is declared by both a " +
                    $"parameter-level and a method-level [CliArgument]. Pick one.");
            }

            // ParameterName / Name are open for internal writes so the parameter-level ctor can be
            // name-less and still plumb through the routing code.
            attr.ParameterName = resolvedName;
            attr.Name = resolvedName;
            result.Add(attr);
        }
        return result;
    }

    /// <summary>
    /// Walk <c>{name}</c> placeholders in the route signature and replace each with a synthesized
    /// <see cref="CliArgumentSegment"/> bound to the matching parameter. Throws at Create time
    /// when a placeholder doesn't match any parameter or when the same parameter is declared twice
    /// (placeholder + <c>[CliArgument]</c> attribute).
    /// </summary>
    /// <remarks>
    /// Runtime detection is the interim answer; the Roslyn analyzer (rule SOL001) promotes the
    /// same check to compile-time. The runtime check stays — it's the defense for users who build
    /// without the analyzer installed.
    /// </remarks>
    private List<CliRouteSegment> ResolveRoutePlaceholders(
        ParameterInfo[] parameters,
        IReadOnlyList<Attribute> attributes,
        List<CliArgumentAttribute> parameterLevelArgs,
        out List<CliArgumentAttribute> placeholderArgs)
    {
        // Method-level [CliArgument(nameof(x), …)] on the same param as a placeholder is a
        // genuine conflict (two routing declarations). Parameter-level [CliArgument(description)]
        // is NOT a conflict — it augments the synthesized argument with a description and is
        // consumed into the placeholder below.
        var methodLevelArgNames = new HashSet<string>(
            attributes.OfType<CliArgumentAttribute>().Select(a => a.ParameterName),
            StringComparer.Ordinal);

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

            if (methodLevelArgNames.Contains(placeholder.Name))
            {
                throw new CliConfigurationException(
                    $"Method '{DeclaringType?.FullName}.{Name}': parameter '{placeholder.Name}' is " +
                    $"declared by both a route placeholder '{{{placeholder.Name}}}' and a method-level " +
                    $"[CliArgument] attribute. Pick one — the placeholder alone is enough for routing; " +
                    $"use a parameter-level [CliArgument(\"description\")] on the parameter itself if " +
                    $"you want to add a description to a placeholder-bound argument.");
            }

            // If the placeholder-bound parameter carries a parameter-level [CliArgument(description)],
            // consume it — its role is description augmentation, not routing.
            var paramLevel = parameterLevelArgs.FirstOrDefault(a => a.ParameterName == placeholder.Name);
            var description = paramLevel?.Description ?? placeholder.Name;
            if (paramLevel is not null)
            {
                parameterLevelArgs.Remove(paramLevel);
            }

            var synthesized = new CliArgumentAttribute(placeholder.Name, description);
            placeholderArgs.Add(synthesized);
            parts[i] = new CliArgumentSegment(synthesized);
        }
        return parts;
    }

    /// <summary>
    /// Build the lookup from <see cref="ParameterInfo"/> to its argument metadata
    /// (attribute + position in <see cref="_routeSegments"/>). Parameters not bound to an
    /// argument are absent from the map; the caller treats them as options or bundles.
    /// </summary>
    private Dictionary<ParameterInfo, (CliArgumentAttribute Attribute, int Position)> MapArgumentsToParameters(
        ParameterInfo[] parameters,
        IReadOnlyList<Attribute> attributes,
        IReadOnlyList<CliArgumentAttribute> placeholderArgs,
        IReadOnlyList<CliArgumentAttribute> parameterLevelArgs)
    {
        var positionByAttribute = new Dictionary<CliArgumentAttribute, int>();
        for (int i = 0; i < _routeSegments.Length; i++)
        {
            if (_routeSegments[i] is CliArgumentSegment arg)
            {
                positionByAttribute[arg.Argument] = i;
            }
        }

        var allArgAttrs = attributes
            .OfType<CliArgumentAttribute>()
            .Concat(placeholderArgs)
            .Concat(parameterLevelArgs);

        var result = new Dictionary<ParameterInfo, (CliArgumentAttribute, int)>();
        foreach (var attribute in allArgAttrs)
        {
            var parameter = parameters.FirstOrDefault(attribute.References);
            if (parameter is null) continue;
            var position = positionByAttribute.TryGetValue(attribute, out var p) ? p : -1;
            result[parameter] = (attribute, position);
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

    public string ToGeneralHelpString() => CliHelpRenderer.RenderGeneralHelp(_model);

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
    /// Tokenizes method attributes into a sequence of <see cref="CliRouteSegment"/>s. Literal
    /// tokens become <see cref="CliLiteralSegment"/>, <c>{name}</c> placeholders become
    /// <see cref="CliPlaceholderSegment"/> (to be resolved later), and method-level
    /// <see cref="CliArgumentAttribute"/>s become <see cref="CliArgumentSegment"/> in their
    /// source-ordered position. The output preserves attribute order so positional routing works.
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

                if (attribute is CliArgumentAttribute argument)
                {
                    return [new CliArgumentSegment(argument)];
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

    public string ToString(string? format, IFormatProvider? formatProvider = null)
    {
        format = format?.ToUpperInvariant();
        switch (format)
        {
            case ("GH"): return ToGeneralHelpString();
            default: return base.ToString();
        }
    }
}
