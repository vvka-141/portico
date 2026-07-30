using System;
using System.Collections.Generic;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-81. `'--cfg[env]' value` was the ONLY spelling for a map option, and `[…]` is a
// filename-expansion pattern. Under zsh — macOS's default login shell, where NOMATCH is on by
// default — an unquoted `--cfg[env]` aborts the command before Portico is invoked:
//
//     % mytool config set --cfg[env] prod
//     zsh: no matches found: --cfg[env]
//
// The failure comes from the shell, so no diagnostic Portico could emit would ever be seen. The
// bracket form is good notation (it is the `?cfg[env]=prod` the CHARTER's HTTP metaphor derives) and
// it survives; the defect was that it was the only notation.
//
// `--cfg key=value` is now canonical. The split lives in CliDictionaryOptionMaterializer, not in
// CliInvocation: the parser is type-blind, so it hands the materializer an ordinary scalar or
// collection capture and the map-ness — known only from the declared type — is what licenses reading
// a pair out of it. That is also why the scalar "everything after the first separator is the value"
// rule needed no change at all; Keep_The_Scalar_First_Separator_Wins_Rule pins that.
//
// Note that no command line below is shell-quoted. These run through Run(string) or Run(string[]),
// where no shell is involved — the tokenizer honours double quotes only, so a `'…'` in a test would
// be a literal apostrophe in the option name. The quoting the docs show is for the user's shell.
// ReSharper disable once InconsistentNaming
public sealed class CliShellSafeMapForm_Should
{
    public sealed class ConfigTool
    {
        public Dictionary<string, string>? Env { get; private set; }
        public Dictionary<string, int>? Ports { get; private set; }
        public Dictionary<string, string[]>? Headers { get; private set; }
        public Dictionary<string, string>? Tags { get; private set; }
        public string? Filter { get; private set; }

        [CliRoute("config")]
        [CliCommandExample("config --env region=eu")]
        public int Configure([CliOption("--env|-e")] Dictionary<string, string> env)
        {
            Env = env;
            return 0;
        }

        [CliRoute("deploy")]
        [CliCommandExample("deploy --ports web=8080")]
        public int Deploy([CliOption("--ports|-p")] Dictionary<string, int> ports)
        {
            Ports = ports;
            return 0;
        }

        [CliRoute("call")]
        [CliCommandExample("call --header Accept=json")]
        public int Call([CliOption("--header|-H")] Dictionary<string, string[]> header)
        {
            Headers = header;
            return 0;
        }

        [CliRoute("search")]
        [CliCommandExample("search --filter name=foo")]
        public int Search([CliOption("--filter|-f")] string filter)
        {
            Filter = filter;
            return 0;
        }

        [CliRoute("tag")]
        [CliCommandExample("tag -t env=prod")]
        public int Tag([CliOption("-t")] Dictionary<string, string> tags)
        {
            Tags = tags;
            return 0;
        }
    }

    public sealed class SecretTool
    {
        [CliRoute("login")]
        [CliCommandExample("login --credential db=hunter2")]
        public int Login([CliOption("--credential", Sensitive = true)] Dictionary<string, string> credential) => 0;
    }

    private static (CliTestRunResult result, ConfigTool tool) Run(string commandLine)
    {
        var tool = new ConfigTool();
        var result = CliTestHarness.ForApplication(cfg => cfg.AddCommands(tool)).Run(commandLine);
        return (result, tool);
    }

    private static (CliTestRunResult result, ConfigTool tool) Run(string[] args)
    {
        var tool = new ConfigTool();
        var result = CliTestHarness.ForApplication(cfg => cfg.AddCommands(tool)).Run(args);
        return (result, tool);
    }

    // --- The two spellings bind the same thing ------------------------------------------------

    [Theory]
    [InlineData("app config --env region=eu")]      // shell-safe, space-separated
    [InlineData("app config --env=region=eu")]      // shell-safe, glued
    [InlineData("app config --env[region] eu")]     // bracket form
    [InlineData("app config --env[region]=eu")]     // bracket form, glued
    public void Bind_Every_Spelling_To_The_Same_Entry(string commandLine)
    {
        var (result, tool) = Run(commandLine);

        result.ExpectExit(0);
        Assert.NotNull(tool.Env);
        Assert.Single(tool.Env!);
        Assert.Equal("eu", tool.Env!["region"]);
    }

    // argv is the path a real shell takes, and it does not pass through the string tokenizer at all
    // — a form proven only on Run(string) is not proven (POR-56).
    public static TheoryData<string[]> ArgvForms() =>
    [
        ["config", "--env", "region=eu"],
        ["config", "--env=region=eu"],
        ["config", "-e", "region=eu"],
        ["config", "-e=region=eu"],
    ];

    [Theory]
    [MemberData(nameof(ArgvForms))]
    public void Bind_The_Shell_Safe_Form_On_The_Argv_Path(string[] argv)
    {
        var (result, tool) = Run(argv);

        result.ExpectExit(0);
        Assert.NotNull(tool.Env);
        Assert.Equal("eu", tool.Env!["region"]);
    }

    [Fact]
    public void Bind_Several_Pairs_From_One_Option_Token()
    {
        var (result, tool) = Run("app config --env region=eu tier=prod");

        result.ExpectExit(0);
        Assert.NotNull(tool.Env);
        Assert.Equal(2, tool.Env!.Count);
        Assert.Equal("eu", tool.Env!["region"]);
        Assert.Equal("prod", tool.Env!["tier"]);
    }

    [Fact]
    public void Bind_Several_Pairs_From_A_Repeated_Option()
    {
        var (result, tool) = Run("app config --env region=eu --env tier=prod");

        result.ExpectExit(0);
        Assert.NotNull(tool.Env);
        Assert.Equal(2, tool.Env!.Count);
        Assert.Equal("eu", tool.Env!["region"]);
        Assert.Equal("prod", tool.Env!["tier"]);
    }

    [Fact]
    public void Bind_The_Two_Spellings_Side_By_Side_In_One_Invocation()
    {
        var (result, tool) = Run("app config --env region=eu --env[tier] prod");

        result.ExpectExit(0);
        Assert.NotNull(tool.Env);
        Assert.Equal(2, tool.Env!.Count);
        Assert.Equal("eu", tool.Env!["region"]);
        Assert.Equal("prod", tool.Env!["tier"]);
    }

    // --- Where the separator falls ------------------------------------------------------------

    // The FIRST '=' splits, so a value may contain as many more as it likes. This is what makes the
    // form usable for the things maps are actually for — connection strings, selectors, filters.
    [Fact]
    public void Split_At_The_First_Separator_Only()
    {
        var (result, tool) = Run("app config --env dsn=host=db;port=5432");

        result.ExpectExit(0);
        Assert.NotNull(tool.Env);
        Assert.Equal("host=db;port=5432", tool.Env!["dsn"]);
    }

    // The mirror image: a KEY containing '=' has no expression in the shell-safe form, and the
    // bracket form is the escape hatch for it. Documented rather than worked around.
    [Fact]
    public void Carry_A_Key_Containing_The_Separator_Only_In_The_Bracket_Form()
    {
        var (result, tool) = Run("app config --env[a=b] x");

        result.ExpectExit(0);
        Assert.NotNull(tool.Env);
        Assert.Equal("x", tool.Env!["a=b"]);
    }

    // The scalar rule POR-56 settled is untouched: for a non-map option everything after the first
    // separator is still the value, verbatim. The map split is a second, type-licensed pass over a
    // value the scalar rule already produced — not a change to that rule.
    [Fact]
    public void Keep_The_Scalar_First_Separator_Wins_Rule()
    {
        var (result, tool) = Run("app search --filter=name=foo");

        result.ExpectExit(0);
        Assert.Equal("name=foo", tool.Filter);
    }

    // --- The multi-valued map keeps its accumulation across the new spelling ------------------

    [Fact]
    public void Accumulate_A_Repeated_Key_When_The_Value_Type_Is_A_Collection()
    {
        var (result, tool) = Run("app call --header Accept=json --header Accept=html");

        result.ExpectExit(0);
        Assert.NotNull(tool.Headers);
        Assert.Equal(new[] { "json", "html" }, tool.Headers!["Accept"]);
    }

    // The form capabilities.md shows beside the bracket one: the key repeats inside a single option
    // token. Both pairs arrive as one collection capture, so this is the branch that iterates.
    [Fact]
    public void Accumulate_A_Key_Repeated_Inside_One_Option_Token()
    {
        var (result, tool) = Run("app call --header Accept=json Accept=html");

        result.ExpectExit(0);
        Assert.NotNull(tool.Headers);
        Assert.Equal(new[] { "json", "html" }, tool.Headers!["Accept"]);
    }

    [Fact]
    public void Accumulate_Across_Both_Spellings_Of_The_Same_Key()
    {
        var (result, tool) = Run("app call --header Accept=json --header[Accept] html");

        result.ExpectExit(0);
        Assert.NotNull(tool.Headers);
        Assert.Equal(new[] { "json", "html" }, tool.Headers!["Accept"]);
    }

    // --- Short options ------------------------------------------------------------------------

    [Theory]
    [InlineData("app config -e region=eu")]
    [InlineData("app config -e=region=eu")]
    public void Bind_The_Shell_Safe_Form_Through_A_Short_Alias(string commandLine)
    {
        var (result, tool) = Run(commandLine);

        result.ExpectExit(0);
        Assert.NotNull(tool.Env);
        Assert.Equal("eu", tool.Env!["region"]);
    }

    // --- Usage errors -------------------------------------------------------------------------

    // A duplicate key is a defined usage error regardless of which spelling introduced it — the two
    // forms share one accumulator, so they cannot disagree about what a key already holds.
    [Fact]
    public void Reject_A_Duplicate_Key_Across_The_Two_Spellings()
    {
        var (result, _) = Run("app config --env region=eu --env[region] us");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("Duplicate key 'region'", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Name_The_Token_That_Carried_No_Separator()
    {
        var (result, _) = Run("app config --env eu");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("expected a key/value pair but received 'eu'", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("--env key=value", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("'--env[key]' value", result.StandardError, StringComparison.Ordinal);
        // The classic positional-after-option surprise is the likeliest cause, so the terminator is
        // named the way every other "this option cannot take that token" error names it (SOL-77).
        Assert.Contains("'--' terminator", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled error", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_An_Empty_Key_In_The_Shell_Safe_Form()
    {
        var (result, _) = Run("app config --env =eu");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("empty map key", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Name_The_Key_When_A_Value_In_The_Shell_Safe_Form_Will_Not_Convert()
    {
        var (result, _) = Run("app deploy --ports web=notanumber");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("web", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("notanumber", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled error", result.StandardError, StringComparison.Ordinal);
    }

    // A `key=value` token is one string carrying the secret, so the separator-missing error must not
    // echo it. Every other message on this path has honoured Sensitive since POR-91; a new throw site
    // is exactly where that gets forgotten.
    [Fact]
    public void Never_Echo_A_Sensitive_Maps_Token_When_The_Separator_Is_Missing()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new SecretTool()))
            .Run("app login --credential hunter2");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("expected a key/value pair", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result.StandardError, StringComparison.Ordinal);
    }

    // Every usage hint on this path — `Use --opt key=value`, `or the bracket form '--opt[key]' value`
    // — is built from the option's FIRST LONG alias. A map option is allowed to have only a short
    // one, and then the hint named a literal `--opt`: an option the user's CLI does not have. An
    // error that sends someone to a flag that does not exist is worse than one that says less, and
    // it is a map option's errors that a user meets most, because the pair syntax is the part people
    // get wrong.
    [Fact]
    public void Name_The_Real_Option_In_The_Hint_When_There_Is_No_Long_Alias()
    {
        var (result, _) = Run("app tag -t oops");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.DoesNotContain("--opt", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("-t key=value", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("'-t[key]' value", result.StandardError, StringComparison.Ordinal);
    }

    // The same hint, reached through the empty-key throw site rather than the missing-separator one,
    // because the placeholder was in the shared helper and every caller inherited it.
    [Fact]
    public void Name_The_Real_Option_In_The_Empty_Key_Hint_Too()
    {
        var (result, _) = Run("app tag -t =eu");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("empty map key", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("--opt", result.StandardError, StringComparison.Ordinal);
    }
}
