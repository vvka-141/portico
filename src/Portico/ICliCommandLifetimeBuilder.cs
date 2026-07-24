using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Portico;

/// <summary>
/// Internal adapter seam for command factories that own an asynchronous per-dispatch lifetime.
/// Kept out of <see cref="ICliApplicationBuilder"/> so container-specific lifetime mechanics do not
/// become part of the public, dependency-free core API.
/// </summary>
internal interface ICliCommandLifetimeBuilder
{
    ICliApplicationBuilder AddCommands(
        Type serviceType,
        Func<object> factory,
        Func<ValueTask> release,
        IEnumerable<CliRouteAttribute> rootRoutes);
}
