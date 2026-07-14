using System;
using System.IO;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-19. Every message that echoed the program name printed the MANAGED ASSEMBLY — "myapp.dll" —
// rather than the command the user typed. A user cannot copy that out of an error and run it;
// `myapp.dll` is not an executable. It showed up on the framework's most-read output path: the
// "Unknown command" error on a first typo.
//
// Cause: Environment.GetCommandLineArgs()[0] is the path to the .dll for an apphost-launched .NET
// app. Environment.ProcessPath is the apphost itself.
//
// The fix applies ONLY to the process-derived path. A caller who supplies argv explicitly —
// CliTestHarness.Run("app.exe echo world"), CliInvocation.FromArgs(string[]) — keeps the name they
// gave, verbatim. Their argv is not ours to reinterpret.
public sealed class CliExecutableName_Should
{
    [Fact]
    public void Resolve_The_Process_Name_Without_A_File_Extension()
    {
        var name = CliInvocation.ProcessExecutableName();

        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.DoesNotContain(".dll", name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".exe", name, StringComparison.OrdinalIgnoreCase);

        // No directory component either — a path is not a program name.
        Assert.DoesNotContain(Path.DirectorySeparatorChar, name);
        Assert.DoesNotContain('/', name);
    }

    [Fact]
    public void Replace_Argv0_On_The_Process_Derived_Path()
    {
        var argv = CliInvocation.ProcessArgv();

        Assert.NotEmpty(argv);
        Assert.Equal(CliInvocation.ProcessExecutableName(), argv[0]);
        Assert.DoesNotContain(".dll", argv[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preserve_An_Explicitly_Supplied_Argv0_Verbatim()
    {
        // The contract of FromArgs(string[]): it echoes the argv you hand it. Eight existing tests
        // in CliInvocation_FromArgs_Should depend on this, and so does CliTestHarness. Reinterpreting
        // a caller's argv would be a different (and worse) bug than the one being fixed.
        var invocation = CliInvocation.FromArgs(["app.exe", "echo", "world"]);

        Assert.Equal("app.exe", invocation.ExecutableName);
    }

    [Fact]
    public void Preserve_The_Harness_Supplied_Name()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new Tool()))
            .Run("app.exe bogus");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("app.exe", result.StandardError, StringComparison.Ordinal);
    }

    private sealed class Tool : ITool
    {
        public int Noop() => 0;
    }

    public interface ITool
    {
        [CliRoute("noop")]
        [CliCommandExample("noop")]
        int Noop();
    }
}
