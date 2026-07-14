using System.IO;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class CliHelpVersion_Should
{
    public sealed class EchoService
    {
        [CliRoute("echo {message}")]
        [CliCommandExample("echo hi")]
        public int Echo(string message) => 0;
    }

    // --- Version: default triggers unchanged -------------------------------------------------

    [Fact]
    public void Honor_Default_Version_Triggers_Long_Form()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithVersion("myapp 1.2.3")
            .WithConsole(console)
            .AddCommands(new EchoService()));

        Assert.Equal(0, app.Run("app.exe --version"));
        Assert.Contains("myapp 1.2.3", console.OutWriter.ToString());
    }

    [Fact]
    public void Honor_Default_Version_Triggers_Short_Form_Uppercase()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithVersion("myapp 1.0")
            .WithConsole(console)
            .AddCommands(new EchoService()));

        Assert.Equal(0, app.Run("app.exe -V"));
        Assert.Contains("myapp 1.0", console.OutWriter.ToString());
    }

    [Fact]
    public void Reject_Lowercase_Short_Form_For_Version_By_Default()
    {
        // -v is conventionally verbose, not version. Default triggers preserve case for shorts.
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithVersion("myapp 1.0")
            .WithConsole(console)
            .AddCommands(new EchoService()));

        var exit = app.Run("app.exe -v");

        Assert.NotEqual(0, exit);  // unknown option / usage error
        Assert.DoesNotContain("myapp 1.0", console.OutWriter.ToString());
    }

    // --- Version: builder delegate customization ---------------------------------------------

    [Fact]
    public void Accept_Version_As_Subcommand_When_Configured()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithVersion(v => v
                .Text("myapp 2.0")
                .Triggers("--version", "-V", "version"))
            .WithConsole(console)
            .AddCommands(new EchoService()));

        Assert.Equal(0, app.Run("app.exe version"));
        Assert.Contains("myapp 2.0", console.OutWriter.ToString());
    }

    [Fact]
    public void Accept_Custom_Version_Short_Form_When_Configured()
    {
        // User who doesn't care about -v/-V conflict can opt in to lowercase.
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithVersion(v => v.Text("myapp 3.0").Triggers("--version", "-v"))
            .WithConsole(console)
            .AddCommands(new EchoService()));

        Assert.Equal(0, app.Run("app.exe -v"));
        Assert.Contains("myapp 3.0", console.OutWriter.ToString());
    }

    [Fact]
    public void Fall_Back_To_Assembly_Version_When_Builder_Omits_Text()
    {
        // .Triggers(...) without .Text(...) → auto-discovered assembly version (non-empty).
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithVersion(v => v.Triggers("--version"))
            .WithConsole(console)
            .AddCommands(new EchoService()));

        Assert.Equal(0, app.Run("app.exe --version"));
        Assert.False(string.IsNullOrWhiteSpace(console.OutWriter.ToString()));
    }

    // --- Help: default triggers unchanged ----------------------------------------------------

    [Fact]
    public void Honor_Default_Help_Triggers()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new EchoService()));

        Assert.Equal(0, app.Run("app.exe --help"));
        Assert.Contains("echo", console.OutWriter.ToString());
    }

    // --- Help: SuppressHelp -----------------------------------------------------------------

    [Fact]
    public void Disable_Help_When_Suppressed()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .SuppressHelp()
            .WithConsole(console)
            .AddCommands(new EchoService()));

        // --help is no longer intercepted; with no route accepting it, the app returns a usage error.
        var exit = app.Run("app.exe --help");
        Assert.NotEqual(0, exit);
        Assert.Empty(console.OutWriter.ToString());
    }

    // --- Help: custom triggers ---------------------------------------------------------------

    [Fact]
    public void Honor_Custom_Help_Triggers()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithHelp(h => h.Triggers("/?", "--manual"))
            .WithConsole(console)
            .AddCommands(new EchoService()));

        // Custom trigger '/?' works.
        Assert.Equal(0, app.Run("app.exe /?"));
        Assert.Contains("echo", console.OutWriter.ToString());
    }

    [Fact]
    public void Reject_Default_Help_Triggers_When_Customized()
    {
        // When the user specifies their own triggers, the defaults are REPLACED (not merged).
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithHelp(h => h.Triggers("--manual"))
            .WithConsole(console)
            .AddCommands(new EchoService()));

        var exit = app.Run("app.exe --help");
        Assert.NotEqual(0, exit);  // --help is no longer a help trigger
    }

    // --- Trigger precedence: a declared route option wins over the built-in trigger (SOL-75) ----

    public sealed class ConnectService
    {
        public string? Host { get; private set; }

        // `-h` is the ecosystem-standard short form for a host option (psql, ssh, curl).
        [CliRoute("connect")]
        [CliCommandExample("connect -h db.example.com")]
        public int Connect([CliOption("--host|-h")] string host)
        {
            Host = host;
            return 0;
        }
    }

    [Fact]
    public void Bind_Declared_Short_Help_Alias_Instead_Of_Firing_Help()
    {
        // `-h` is declared by the matched route, so it must bind to --host and run the command
        // rather than firing the built-in help path.
        var console = new StringCliConsole();
        var svc = new ConnectService();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(svc));

        Assert.Equal(0, app.Run("app.exe connect -h db.example.com"));
        Assert.Equal("db.example.com", svc.Host);
    }

    public sealed class AuditService
    {
        public bool Audited { get; private set; }

        // A command that deliberately claims `-V` for its own use.
        [CliRoute("scan")]
        [CliCommandExample("scan -V")]
        public int Scan([CliOption("--audit|-V")] CliFlag? audit = null)
        {
            Audited = audit is not null;
            return 0;
        }
    }

    [Fact]
    public void Bind_Declared_Version_Alias_Instead_Of_Printing_Version()
    {
        // `-V` is declared by the matched route, so it must bind to --audit and run the command
        // rather than firing the built-in version path — even though version text is configured.
        var console = new StringCliConsole();
        var svc = new AuditService();
        var app = CliApplication.Create(cfg => cfg
            .WithVersion("myapp 4.0")
            .WithConsole(console)
            .AddCommands(svc));

        Assert.Equal(0, app.Run("app.exe scan -V"));
        Assert.True(svc.Audited);
        Assert.DoesNotContain("myapp 4.0", console.OutWriter.ToString());
    }

    public sealed class VerboseEmitService
    {
        public bool Ran { get; private set; }

        [CliRoute("emit")]
        [CliCommandExample("emit --verbose")]
        public int Emit([CliOption("--verbose|-v")] CliFlag? verbose = null)
        {
            Ran = true;
            return 0;
        }
    }

    [Fact]
    public void Preserve_Version_Trigger_When_Route_Declares_Lowercase_Verbose()
    {
        // A route declaring `-v` (verbose) must NOT swallow the case-sensitive `-V` version trigger:
        // the -V/-v distinction is the whole point of the convention.
        var console = new StringCliConsole();
        var svc = new VerboseEmitService();
        var app = CliApplication.Create(cfg => cfg
            .WithVersion("myapp 5.0")
            .WithConsole(console)
            .AddCommands(svc));

        Assert.Equal(0, app.Run("app.exe emit -V"));
        Assert.Contains("myapp 5.0", console.OutWriter.ToString());
        Assert.False(svc.Ran);
    }

    public sealed class GreetService
    {
        public bool Ran { get; private set; }

        [CliRoute("greet {name}")]
        [CliCommandExample("greet Ada")]
        public int Greet(string name)
        {
            Ran = true;
            return 0;
        }
    }

    [Fact]
    public void Fire_Help_When_Matched_Route_Does_Not_Declare_The_Alias()
    {
        // Precedence direction two: a route that does NOT declare `-h` still yields to built-in
        // help, so `--help`/`-h` keep working on ordinary commands.
        var console = new StringCliConsole();
        var svc = new GreetService();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(svc));

        Assert.Equal(0, app.Run("app.exe greet Ada -h"));
        Assert.Contains("greet", console.OutWriter.ToString());
        Assert.False(svc.Ran);
    }

    public sealed class QueryService
    {
        public string? Query { get; private set; }

        [CliRoute("query {q}")]
        [CliCommandExample("query hello")]
        public int Run(string q)
        {
            Query = q;
            return 0;
        }
    }

    [Fact]
    public void Treat_Help_Looking_Segment_As_Argument_Of_The_Matched_Route()
    {
        // `?` matches the help-signal pattern, but here it is the matched route's argument value,
        // so the command runs with q = "?" instead of the built-in help firing.
        var console = new StringCliConsole();
        var svc = new QueryService();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(svc));

        Assert.Equal(0, app.Run("app.exe query ?"));
        Assert.Equal("?", svc.Query);
    }
}
