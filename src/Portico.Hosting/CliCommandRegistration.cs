using System;
using System.Collections.Generic;
using System.Linq;

namespace Portico.Hosting;

/// <summary>
/// One command contract registered into the service collection, and the root route it is mounted
/// under. Collected by <see cref="CliHostExtensions.AddPorticoCommands{TContract, TImplementation}"/>
/// and replayed onto the <see cref="ICliApplicationBuilder"/> when the host runs.
/// </summary>
/// <remarks>
/// A separate registration record — rather than building the <see cref="CliApplication"/> eagerly —
/// is what keeps <c>builder.Services</c> the single place a service is declared. The CLI is
/// assembled from the container, not alongside it.
/// </remarks>
internal sealed class CliCommandRegistration(Type contractType, IReadOnlyList<string> rootRoutes)
{
    public Type ContractType { get; } = contractType;

    public IEnumerable<CliRouteAttribute> RootRoutes { get; } =
        rootRoutes.Select(route => new CliRouteAttribute(route)).ToArray();
}
