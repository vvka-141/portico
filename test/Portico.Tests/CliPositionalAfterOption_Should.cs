using System;
using System.Collections.Generic;
using Portico.Testing;
using Xunit;

namespace Portico;

// Positional-after-options behavior. Portico resolves a positional that follows an option only via
// the explicit POSIX `--` terminator, and **that decision is recorded** in `docs/ROADMAP.md` under
// Resolved decisions, with its rationale and the bar to reopen it (POR-82). This file is the
// executable half of that record: the decision is defensible only because the rejection is loud and
// teaches the fix, so every claim the ROADMAP entry makes about the diagnostic is asserted here.
//
// The comment this replaces cited `CommandLine/ROADMAP.md` — a path in the *origin* repository. No
// such file is in this repo, and Portico's own ROADMAP did not carry the decision at all: the
// behaviour was tested and the reasoning was nowhere. That is the drift POR-82 closed.
// ReSharper disable once InconsistentNaming
public sealed class CliPositionalAfterOption_Should
{
    public sealed class FlagService
    {
        [CliRoute("check")]
        [CliCommandExample("check -v")]
        public int Check([CliOption("--verbose|-v")] CliFlag? verbose = null) => 0;
    }

    public sealed class BuildService
    {
        [CliRoute("build")]
        [CliCommandExample("build --output out.dll")]
        public int Build([CliOption("--output|-o")] string output) => 0;
    }

    public sealed class CompileService
    {
        public string? Source { get; private set; }
        public string? Output { get; private set; }

        [CliRoute("compile {source}")]
        [CliCommandExample("compile main.cs")]
        public int Compile(string source, [CliOption("--output|-o")] string output = "a.out")
        {
            Source = source;
            Output = output;
            return 0;
        }
    }

    private static (int exit, StringCliConsole console, T svc) Run<T>(T svc, string commandLine)
        where T : class
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg.WithConsole(console).AddCommands(svc));
        return (app.Run(commandLine), console, svc);
    }

    // --- Interim targeted error (implicit form is not supported) ------------------------------

    [Fact]
    public void Flag_That_Swallows_A_Positional_Names_The_Token_And_Points_At_Terminator()
    {
        var (exit, console, _) = Run(new FlagService(), "app.exe check -v file.txt");

        Assert.Equal(CliExitException.UsageErrorExitCode, exit);
        var err = console.ErrorWriter.ToString();
        Assert.Contains("file.txt", err);
        Assert.Contains("--", err);                    // reminds the user about the terminator
        Assert.DoesNotContain("Unhandled error", err);
    }

    [Fact]
    public void Scalar_That_Swallows_Extra_Tokens_Names_Them_And_Points_At_Terminator()
    {
        var (exit, console, _) = Run(new BuildService(), "app.exe build --output out.dll main.cs");

        Assert.Equal(CliExitException.UsageErrorExitCode, exit);
        var err = console.ErrorWriter.ToString();
        Assert.Contains("out.dll", err);
        Assert.Contains("main.cs", err);
        Assert.Contains("--", err);
    }

    // --- The documented `--` terminator resolves positional-after-option ----------------------

    [Fact]
    public void Terminator_Resolves_A_Positional_That_Follows_An_Option()
    {
        var (exit, _, svc) = Run(new CompileService(), "app.exe compile --output out.dll -- main.cs");

        Assert.Equal(0, exit);
        Assert.Equal("main.cs", svc.Source);
        Assert.Equal("out.dll", svc.Output);
    }

    [Fact]
    public void Natural_Order_Positional_Before_Option_Works()
    {
        var (exit, _, svc) = Run(new CompileService(), "app.exe compile main.cs --output out.dll");

        Assert.Equal(0, exit);
        Assert.Equal("main.cs", svc.Source);
        Assert.Equal("out.dll", svc.Output);
    }

    // --- The shape POR-82 is named about: the route DOES declare the positional -----------------
    //
    // The two error cases above are routes with no positional at all, so the failure surfaces on the
    // option (`--output` got two values; a flag got one). The shape a user actually types is a route
    // that *has* a positional — and there the failure surfaces earlier, as a route-shape mismatch,
    // because the option consumed the tokens before any argument count could be satisfied.
    //
    // POR-82's description asserted this path already "names the offending token and points at `--`".
    // It did not: it reported `Command 'emit {source}' expects 1 argument, got 0.` and stopped. The
    // terminator hint POR-115 added was gated on an *unrecognized* option, and a correctly-spelled
    // `--output` never reached it. Every case below was red before that gate grew a second branch.

    public sealed class EmitService
    {
        public string? Source { get; private set; }

        [CliRoute("emit {source}")]
        [CliCommandExample("emit main.cs")]
        public int Emit(string source, [CliOption("--output|-o")] string output = "a.out")
        {
            Source = source;
            return 0;
        }

        [CliRoute("copy {source} {dest}")]
        [CliCommandExample("copy a b")]
        public int Copy(string source, string dest, [CliOption("--force|-f")] CliFlag? force = null) => 0;

        [CliRoute("push {image}")]
        [CliCommandExample("push app:1")]
        public int Push(string image, [CliOption("--token", Sensitive = true)] string token = "") => 0;
    }

    private static CliTestRunResult RunEmit(string commandLine) =>
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(new EmitService())).Run(commandLine);

    [Theory]
    [InlineData("app emit --output out.dll main.cs")]
    [InlineData("app emit -o out.dll main.cs")]
    public void Name_The_Token_A_Declared_Option_Swallowed(string commandLine)
    {
        var result = RunEmit(commandLine);

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        // The count line still leads — it is the literal truth about the route.
        Assert.Contains("expects 1 argument, got 0", result.StandardError, StringComparison.Ordinal);
        // …and is now followed by the rule and the fix, naming the token the user typed.
        Assert.Contains("consumed 2 values", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("belongs to that option", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("If 'main.cs' is a positional argument", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("'--' terminator", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled error", result.StandardError, StringComparison.Ordinal);
    }

    // A flag consumed both positionals. The hint pluralizes and proposes moving both, because the
    // route is short by two and a greedy parse appended exactly those two last.
    [Fact]
    public void Name_Every_Token_A_Flag_Swallowed_When_The_Route_Wants_Several()
    {
        var result = RunEmit("app copy --force a b");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("expects 2 arguments, got 0", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Option '--force' consumed 2 values", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("If 'a', 'b' are positional arguments", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("-- a b", result.StandardError, StringComparison.Ordinal);
    }

    // Only the trailing values are proposed: `--output` legitimately owns `out.dll`, and the route is
    // short by one, so exactly one token is a candidate. Proposing both would advise a broken command.
    [Fact]
    public void Propose_Only_As_Many_Trailing_Tokens_As_The_Route_Is_Short_Of()
    {
        var result = RunEmit("app emit --output out.dll main.cs");

        Assert.DoesNotContain("'out.dll' is a positional", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("-- out.dll", result.StandardError, StringComparison.Ordinal);
    }

    // `capabilities.md` and the ROADMAP entry both quote this message as three lines: the count, the
    // rule, the fix. One wrapped paragraph in a terminal buries the fix, so the shape is part of the
    // claim — and a doc block quoting an output is only true while the output keeps that shape.
    [Fact]
    public void Print_The_Count_Then_The_Rule_Then_The_Fix()
    {
        var lines = RunEmit("app emit --output out.dll main.cs")
            .StandardError
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("Command 'emit {source}' expects", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("Option '--output' consumed", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("If 'main.cs' is a positional argument", lines[2], StringComparison.Ordinal);
    }

    // The suggestion the message prints must be a command that works. This runs it.
    [Fact]
    public void Print_A_Suggestion_That_Actually_Binds()
    {
        RunEmit("app emit --output out.dll main.cs")
            .ExpectError("-- main.cs");

        var fixedUp = CliTestHarness.ForApplication(cfg => cfg.AddCommands(new EmitService()));
        fixedUp.Run("app emit --output out.dll -- main.cs").ExpectExit(0);
    }

    // No route has bound yet, but a near-miss names a concrete route, so its `Sensitive` declarations
    // are readable — unlike the unknown-command path, which renders no values at all because it has no
    // metadata to consult. A secret swallowed as a positional candidate is redacted, not echoed
    // (POR-91's constraint, which POR-82 was warned not to undo).
    [Fact]
    public void Redact_A_Sensitive_Options_Swallowed_Value()
    {
        var result = RunEmit("app push --token s3cret app:1");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("Option '--token' consumed 2 values", result.StandardError, StringComparison.Ordinal);
        Assert.Contains(CliInvocation.Redacted, result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", result.StandardError, StringComparison.Ordinal);
        // The candidate was `app:1`, not the secret — but the framework cannot tell one value of a
        // sensitive option from another, so it redacts the lot. An over-redacted hint is the correct
        // trade against printing a credential into a container's log stream.
        Assert.DoesNotContain("app:1", result.StandardError, StringComparison.Ordinal);
    }

    // Cause 1 still wins where it applies: an unrecognized option is a better explanation than a
    // greedy declared one, and POR-115's branch is unchanged.
    [Fact]
    public void Prefer_The_Unrecognized_Option_Explanation_When_There_Is_One()
    {
        var result = RunEmit("app emit --typo main.cs");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("'--typo'", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("meant as positional", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("consumed", result.StandardError, StringComparison.Ordinal);
    }

    // --- Why implicit resolution stays parked: the undecidable case ----------------------------
    //
    // This is the evidence the ROADMAP entry rests on, and the reason "just resolve it implicitly" is
    // not a small change. A *variadic* option followed by a positional has no correct greedy answer:
    // `--tags a b main.cs` is indistinguishable from three tags. Any implicit rule has to decide it by
    // consulting the route's positional arity — which means tokenizing after route matching, and route
    // matching currently consumes the tokenizer's output. That is a dependency inversion in the most
    // load-bearing file in src/Portico/, not merely more code.

    public sealed class TagService
    {
        public IReadOnlyList<string>? Tags { get; private set; }
        public string? Source { get; private set; }

        [CliRoute("tag {source}")]
        [CliCommandExample("tag main.cs")]
        public int Tag(string source, [CliOption("--tags")] List<string>? tags = null)
        {
            Source = source;
            Tags = tags;
            return 0;
        }
    }

    [Fact]
    public void Refuse_A_Variadic_Option_Followed_By_A_Positional_Rather_Than_Guess()
    {
        var tool = new TagService();
        var result = CliTestHarness.ForApplication(cfg => cfg.AddCommands(tool)).Run("app tag --tags a b main.cs");

        // Three values for --tags and nothing for {source}. There is no token count that makes this
        // both a legal tag list and a legal argument list, which is exactly why the greedy rule is
        // kept and the terminator is required.
        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("expects 1 argument, got 0", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("If 'main.cs' is a positional argument", result.StandardError, StringComparison.Ordinal);
        Assert.Null(tool.Source);
    }

    [Fact]
    public void Bind_A_Variadic_Option_And_A_Positional_When_The_Terminator_Separates_Them()
    {
        var tool = new TagService();
        CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(tool))
            .Run("app tag --tags a b -- main.cs")
            .ExpectExit(0);

        Assert.Equal("main.cs", tool.Source);
        Assert.Equal(new[] { "a", "b" }, tool.Tags);
    }

    [Fact]
    public void Bind_A_Variadic_Option_After_The_Positional_With_No_Ceremony()
    {
        var tool = new TagService();
        CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(tool))
            .Run("app tag main.cs --tags a b")
            .ExpectExit(0);

        Assert.Equal("main.cs", tool.Source);
        Assert.Equal(new[] { "a", "b" }, tool.Tags);
    }
}
