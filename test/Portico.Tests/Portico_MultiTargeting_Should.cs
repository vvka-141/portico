using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// Portico multi-targets net8.0 and net10.0 and ships NO preprocessor conditional in src/. That is a
// property rather than an accident, and adding the second target cost exactly two source changes,
// both solved in the SHARED form and commented where they sit:
//
//   * CliCancellationTokenTypeConverter uses the [GeneratedRegex] METHOD form, because the
//     partial-PROPERTY form only exists on net9.0+.
//   * CliMethodInvoker spells Enumerable.Reverse(x) rather than x.Reverse(), because on net8.0 a
//     CliMiddleware[] binds to MemoryExtensions.Reverse(Span<T>) — which reverses IN PLACE and
//     returns void.
//
// The second one is the argument for this gate existing. That is the same source text meaning
// something DIFFERENT per target framework, and the only reason the compiler caught it rather than
// the middleware pipeline tearing down in registration order on one TFM and reverse on the other
// was a chained ToArray(). A `#if` would have made that class of divergence invisible instead of
// impossible: refusing conditionals is what forces both targets to compile the same text, so a
// difference between them has to surface as a build error somewhere.
//
// Nothing enforced this before. The property was stated in CLAUDE.md and in the hardening playbook,
// which is exactly the "true today, nothing stops it decaying" case.
public sealed class Portico_MultiTargeting_Should
{
    /// <summary>
    /// No <c>#if</c> / <c>#elif</c> / <c>#else</c> / <c>#endif</c> anywhere in the shipped source.
    /// </summary>
    /// <remarks>
    /// <c>#pragma</c>, <c>#nullable</c>, <c>#region</c>, <c>#warning</c> and <c>#error</c> are not
    /// conditionals and are not matched — the pattern anchors on the directive keyword.
    /// <para>
    /// <c>templates/</c> is deliberately out of scope. Its <c>#if (async)</c> blocks are
    /// <c>dotnet new</c> template symbols, resolved and removed by the template engine before the
    /// file is ever handed to a compiler, so they are a different mechanism that happens to share a
    /// spelling. <c>test/</c> and <c>examples/</c> are out of scope too: the invariant is about what
    /// Portico ships.
    /// </para>
    /// </remarks>
    [Fact]
    public void Compile_The_Same_Source_Text_For_Every_Target()
    {
        var conditional = new Regex(@"^\s*#\s*(if|elif|else|endif)\b", RegexOptions.CultureInvariant);
        var root = Path.Combine(RepositoryRoot(), "src");

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            // bin/ and obj/ carry generated files (AssemblyInfo, editorconfig shims) that are not
            // anybody's source and are regenerated on every build.
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (conditional.IsMatch(lines[i]))
                {
                    offenders.Add($"{Path.GetRelativePath(RepositoryRoot(), file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "src/ contains a preprocessor conditional:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders) + Environment.NewLine +
            "Portico compiles the same source text for every target framework, which is what makes a " +
            "per-target behaviour difference show up as a build error instead of a runtime surprise. " +
            "Find the shared form — CliCancellationTokenTypeConverter and CliMethodInvoker are the two " +
            "worked examples. If a conditional is genuinely unavoidable, that is a decision worth a " +
            "ticket and a note in CLAUDE.md, not a quiet edit to this test.");
    }

    /// <summary>
    /// The rule above is only meaningful while there is more than one target to diverge between.
    /// </summary>
    /// <remarks>
    /// Without this, dropping a target framework would leave a green, vacuous test behind — the
    /// no-conditional rule would still pass and would no longer be saying anything. The analyzer
    /// projects override this to <c>netstandard2.0</c>, which Roslyn requires; that override is not
    /// what this reads.
    /// </remarks>
    [Fact]
    public void Declare_Both_Target_Frameworks()
    {
        var props = File.ReadAllText(Path.Combine(RepositoryRoot(), "Directory.Build.props"));

        Assert.True(
            props.Contains("<TargetFrameworks>net8.0;net10.0</TargetFrameworks>", StringComparison.Ordinal),
            "Directory.Build.props no longer declares <TargetFrameworks>net8.0;net10.0</TargetFrameworks>. " +
            "If a target was added or dropped deliberately, update this test and CLAUDE.md's 'Build, " +
            "test, run' section together — and note that Compile_The_Same_Source_Text_For_Every_Target " +
            "becomes vacuous if only one target is left.");
    }

    private static string RepositoryRoot()
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
