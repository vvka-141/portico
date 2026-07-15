using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Portico.Reflection;

internal sealed record CliContext
{
    private readonly ImmutableArray<CliMiddleware> _globalOptionBundles;
    private readonly ICliConsole _console;
    public static readonly CliContext Empty = new([], [], SystemCliConsole.Instance);


    public CliContext(
        IEnumerable<CliRouteAttribute> rootRoutes,
        IEnumerable<CliMiddleware> globalOptionBundles,
        ICliConsole console)
    {
        RootRoutes = [..rootRoutes];
        _globalOptionBundles = [..globalOptionBundles];
        _console = console;
        GlobalOptions = [.. _globalOptionBundles.SelectMany(bundle => bundle.GetOptions()).Distinct()];
    }


    public ImmutableArray<CliRouteAttribute> RootRoutes { get; }

    public CliMiddleware[] ToGlobalOptionBundles(CliInvocation invocation)
    {
        var bundles = new List<CliMiddleware>(_globalOptionBundles.Length);
        foreach (var bundle in _globalOptionBundles)
        {
            var clone = bundle.Clone();
            clone.AttachConsole(_console);   // inject the console before hooks run (SOL-80)
            clone.PopulateFrom(invocation);
            bundles.Add(clone);
        }
        return bundles.ToArray();
    }

    public ImmutableArray<ICliOptionMemberInfo> GlobalOptions { get; }
}