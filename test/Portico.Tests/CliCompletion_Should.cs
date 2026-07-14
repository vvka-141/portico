using System.IO;
using Portico.Completion;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class CliCompletion_Should
{
    public interface IDemoCommands
    {
        [CliRoute("init")]
        [CliArgument(nameof(path), "project path")]
        [CliCommandExample("init .")]
        int Init(string path);

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

    private static CliApplication NewApplication() =>
        CliApplication.Create(cfg => cfg.AddCommands(new DemoCommands()));

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
}
