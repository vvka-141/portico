using System;
using System.IO;
using Xunit;

namespace Portico;

/// <summary>
/// The repository root, found by walking up from the test binary until <c>portico.sln</c> appears.
/// </summary>
/// <remarks>
/// Ten test classes read a tracked file to check a documented claim against the code, and every one
/// of them had pasted the same walk as a private helper. The hazard is not the duplication itself
/// but that all ten copies keep compiling: a change to how the root is located — a different marker
/// file, a nullable-root fallback, a check that the walk terminated somewhere sane — would be
/// applied to whichever copy the author happened to be reading and leave the other nine behind,
/// with nothing failing to say so.
/// </remarks>
internal static class RepositoryPaths
{
    internal static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "portico.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
