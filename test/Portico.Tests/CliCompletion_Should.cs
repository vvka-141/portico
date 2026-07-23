using System;
using System.IO;
using System.Linq;
using Portico.Completion;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class CliCompletion_Should
{
    public interface IDemoCommands
    {
        [CliRoute("init {path}")]
        [CliCommandExample("init .")]
        int Init([CliArgument("project path")] string path);

        [CliRoute("db migrate")]
        [CliCommandExample("db migrate")]
        int Migrate();

        [CliRoute("db status")]
        [CliCommandExample("db status")]
        int Status();
    }

    public sealed class DemoCommands : IDemoCommands
    {
        public int Init(string path) => 0;
        public int Migrate() => 0;
        public int Status() => 0;
    }

    /// <summary>
    /// The interior-placeholder shape: <c>tenant {env} migrate</c> has literals on BOTH sides of an
    /// argument. Deleting the placeholder and rejoining would produce <c>tenant migrate</c>, which is
    /// not a route this application dispatches.
    /// </summary>
    public interface IInteriorCommands
    {
        [CliRoute("tenant {env} migrate")]
        [CliCommandExample("tenant prod migrate")]
        int Migrate([CliArgument("environment")] string env);

        [CliRoute("tenant backup")]
        [CliCommandExample("tenant backup")]
        int Backup();
    }

    public sealed class InteriorCommands : IInteriorCommands
    {
        public int Migrate(string env) => 0;
        public int Backup() => 0;
    }

    private static CliApplication NewApplication() =>
        CliApplication.Create(cfg => cfg.AddCommands(new DemoCommands()));

    private static string EmitInterior(CliCompletionShell shell)
    {
        var buffer = new StringWriter();
        CliApplication
            .Create(cfg => cfg.AddCommands(new InteriorCommands()))
            .EmitCompletion(shell, "demo", buffer);
        return buffer.ToString();
    }

    [Fact]
    public void Expose_RouteSignatures_From_Application()
    {
        var signatures = NewApplication().GetRouteSignatures();
        Assert.Contains("init {path}", signatures);
        Assert.Contains("db migrate", signatures);
        Assert.Contains("db status", signatures);
    }

    [Fact]
    public void Emit_Bash_Script_Containing_Every_Literal_Route()
    {
        var buffer = new StringWriter();
        NewApplication().EmitCompletion(CliCompletionShell.Bash, "demo", buffer);
        var script = buffer.ToString();

        Assert.Contains("complete -F _demo_complete demo", script);
        Assert.Contains("__PORTICO_ROUTES__", script);
        Assert.Contains("init", script);            // placeholder stripped
        Assert.Contains("db migrate", script);
        Assert.Contains("db status", script);
        Assert.DoesNotContain("{path}", script);    // arg placeholders must not leak
    }

    [Fact]
    public void Emit_Zsh_Script_With_Compdef_Block()
    {
        var buffer = new StringWriter();
        NewApplication().EmitCompletion(CliCompletionShell.Zsh, "demo", buffer);
        var script = buffer.ToString();

        Assert.Contains("#compdef demo", script);
        Assert.Contains("compdef _demo demo", script);
        Assert.Contains("\"db migrate\"", script);
        Assert.Contains("\"db status\"", script);
        Assert.DoesNotContain("{path}", script);
    }

    [Fact]
    public void Emit_PowerShell_Script_With_Register_ArgumentCompleter()
    {
        var buffer = new StringWriter();
        NewApplication().EmitCompletion(CliCompletionShell.PowerShell, "demo", buffer);
        var script = buffer.ToString();

        Assert.Contains("Register-ArgumentCompleter -Native -CommandName demo", script);
        Assert.Contains("'db migrate'", script);
        Assert.Contains("'db status'", script);
        Assert.DoesNotContain("{path}", script);
    }

    [Theory]
    [InlineData(CliCompletionShell.Bash)]
    [InlineData(CliCompletionShell.Zsh)]
    [InlineData(CliCompletionShell.PowerShell)]
    public void Never_Propose_A_Route_Welded_Across_An_Interior_Placeholder(CliCompletionShell shell)
    {
        var script = EmitInterior(shell);

        // "tenant migrate" is what deleting {env} and rejoining produces. The application exits 2 on it.
        Assert.DoesNotContain("tenant migrate", script);
        Assert.DoesNotContain("{env}", script);
        Assert.Contains("tenant backup", script);
    }

    [Fact]
    public void Offer_The_Literal_Prefix_Up_To_An_Interior_Placeholder()
    {
        var script = EmitInterior(CliCompletionShell.Bash);
        var table = RouteTableOf(script);

        Assert.Equal(["tenant", "tenant backup"], table);
    }

    [Fact]
    public void Truncate_A_Trailing_Placeholder_As_Before()
    {
        var buffer = new StringWriter();
        NewApplication().EmitCompletion(CliCompletionShell.Bash, "demo", buffer);

        Assert.Equal(["db migrate", "db status", "init"], RouteTableOf(buffer.ToString()));
    }

    /// <summary>Reads back the heredoc route table the bash emitter writes between its two markers.</summary>
    private static string[] RouteTableOf(string bashScript)
    {
        var lines = bashScript.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var start = Array.FindIndex(lines, l => l.EndsWith("<<'__PORTICO_ROUTES__'", StringComparison.Ordinal));
        var end = Array.FindIndex(lines, start + 1, l => l == "__PORTICO_ROUTES__");
        Assert.True(start >= 0 && end > start, "the bash script must delimit its route table");
        return lines[(start + 1)..end];
    }
}
