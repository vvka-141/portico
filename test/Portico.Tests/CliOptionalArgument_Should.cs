using System;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-18. `string target = "default"` on a [CliArgument] is C# for "this is optional" — but the
// framework ignored the default and still required the positional, so `build` and `build --verbose`
// both failed with "Unknown command". The author got no signal at all: the default was simply inert.
//
// Resolution 1 (of the two the ticket offered): support optional TRAILING positionals. This is the
// direct analogue of ASP.NET route defaults, so it sits inside the CHARTER's HTTP metaphor rather
// than extending it. Optionality is confined to the tail; anything else is a config error.
public sealed class CliOptionalArgument_Should
{
    public interface ITool
    {
        [CliRoute("build {target}")]
        [CliCommandExample("build")]
        [CliCommandExample("build --verbose")]
        [CliCommandExample("build release")]
        int Build(
            [CliOption("--verbose|-v")] CliFlag? verbose = null,
            [CliArgument("the build target")] string target = "default");

        [CliRoute("copy {source} {destination}")]
        [CliCommandExample("copy src")]
        [CliCommandExample("copy src dst")]
        int Copy(
            [CliArgument("source path")] string source,
            [CliArgument("destination path")] string destination = "./out");
    }

    private sealed class Tool : ITool
    {
        public int Build(CliFlag? verbose, string target)
        {
            Console.WriteLine($"target=[{target}] v={verbose is not null}");
            return 0;
        }

        public int Copy(string source, string destination)
        {
            Console.WriteLine($"src=[{source}] dst=[{destination}]");
            return 0;
        }
    }

    private static CliTestRunResult Run(string commandLine) =>
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(new Tool())).Run(commandLine);

    [Fact]
    public void Bind_The_Csharp_Default_When_The_Positional_Is_Omitted()
    {
        var result = Run("app.exe build");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("target=[default]", result.StandardOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Match_The_Route_With_Options_But_No_Positional()
    {
        // The exact repro from the ticket: this used to be "Unknown command".
        var result = Run("app.exe build --verbose");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("target=[default] v=True", result.StandardOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Still_Bind_A_Supplied_Positional()
    {
        var result = Run("app.exe build release --verbose");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("target=[release] v=True", result.StandardOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Keep_A_Required_Positional_Required()
    {
        // 'source' has no default — omitting it must still fail, not silently bind null.
        var result = Run("app.exe copy");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void Bind_Only_The_Optional_Tail_When_Partially_Supplied()
    {
        var supplied = Run("app.exe copy a b");
        Assert.Equal(0, supplied.ExitCode);
        Assert.Contains("src=[a] dst=[b]", supplied.StandardOut, StringComparison.Ordinal);

        var omitted = Run("app.exe copy a");
        Assert.Equal(0, omitted.ExitCode);
        Assert.Contains("src=[a] dst=[./out]", omitted.StandardOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_Too_Many_Positionals()
    {
        var result = Run("app.exe build release extra");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void Render_An_Optional_Positional_As_Square_Brackets_In_Help()
    {
        // <NAME> is required, [NAME] is optional — the git/docker/dotnet convention.
        var result = Run("app.exe build --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("[TARGET]", result.StandardOut, StringComparison.Ordinal);
        Assert.DoesNotContain("<TARGET>", result.StandardOut, StringComparison.Ordinal);

        var copyHelp = Run("app.exe copy --help");
        Assert.Contains("<SOURCE>", copyHelp.StandardOut, StringComparison.Ordinal);
        Assert.Contains("[DESTINATION]", copyHelp.StandardOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_Every_Declared_Example()
    {
        // The wedge: the examples above ("build", "build --verbose", "build release", ...) are the
        // very forms that used to fail. If the fix were wrong, the contract validator would say so.
        var notDispatched = 0;
        new CliContractValidator<ITool>().Validate(
            onNotInvoked: (example, reason) => notDispatched++);

        Assert.Equal(0, notDispatched);
    }

    [Fact]
    public void Reject_An_Optional_Argument_Followed_By_A_Required_One()
    {
        // Unresolvable: given one token for two slots, the framework cannot know which was omitted.
        // Fail loudly at Create rather than binding something arbitrary at dispatch.
        var exception = Assert.Throws<CliConfigurationException>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new Ambiguous())));

        Assert.Contains("must be last", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // C# forces optional parameters last in the SIGNATURE — but the ROUTE order is independent of
    // it. Here 'optional' sits at route position 0 and the required 'required' at position 1, so
    // the hazard is real even though the C# signature is legal.
    public interface IAmbiguous
    {
        [CliRoute("x {optional} {required}")]
        [CliCommandExample("x a b")]
        int X(
            [CliArgument("required")] string required,
            [CliArgument("optional")] string optional = "d");
    }

    private sealed class Ambiguous : IAmbiguous
    {
        public int X(string required, string optional) => 0;
    }
}
