using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Portico;

/// <summary>
/// CHARTER §6.5: "the analyzer moves the failure into the edit loop, it does not replace the check."
/// A user who builds without the analyzers installed — a transitive reference with analyzer assets
/// switched off, a non-SDK build, an older toolchain — must still be told at
/// <c>CliApplication.Create</c> rather than getting a CLI that misdispatches.
/// </summary>
/// <remarks>
/// <para>
/// Every live rule is classified here as either backed by a runtime check or deliberately exempt,
/// and both halves are executed: a backed rule must throw <see cref="CliConfigurationException"/>,
/// and an exempt rule must <em>not</em> throw. Asserting the exemptions matters as much as asserting
/// the backstops — it is what distinguishes "this shape is legal" from "nobody got round to
/// checking it", which is exactly the confusion that let the charter claim more coverage than
/// existed.
/// </para>
/// <para>
/// The final test cross-references the table against the analyzers' own <c>SupportedDiagnostics</c>,
/// so a new rule cannot ship without its author deciding, in writing, which side it falls on.
/// </para>
/// </remarks>
public sealed class PorticoRuntimeBackstops_Should
{
    // ---- POR001: a route placeholder matching no parameter -----------------------------------
    public interface IPor001 { [CliRoute("deploy {target}")] [CliCommandExample("deploy x")] int Deploy(string environment); }
    private sealed class Por001 : IPor001 { public int Deploy(string environment) => 0; }

    // ---- POR002: two methods on one type claiming the same route ------------------------------
    public interface IPor002
    {
        [CliRoute("init")] [CliCommandExample("init")] int A();
        [CliRoute("init")] [CliCommandExample("init")] int B();
    }
    private sealed class Por002 : IPor002 { public int A() => 0; public int B() => 0; }

    // ---- POR003: a malformed [CliOption] spec --------------------------------------------------
    public interface IPor003 { [CliRoute("go")] [CliCommandExample("go")] int Go([CliOption("verbose")] string v = ""); }
    private sealed class Por003 : IPor003 { public int Go(string v) => 0; }

    // ---- POR004: a [CliRoute] with no [CliCommandExample] — EXEMPT -----------------------------
    public interface IPor004 { [CliRoute("go")] int Go(); }
    private sealed class Por004 : IPor004 { public int Go() => 0; }

    // ---- POR005: a [CliArgument] with no matching placeholder ---------------------------------
    public interface IPor005
    {
        [CliRoute("cp {dest}")] [CliCommandExample("cp a")] int Copy(string dest, [CliArgument("src")] string src);
    }
    private sealed class Por005 : IPor005 { public int Copy(string dest, string src) => 0; }

    // ---- POR006: a CliOptions bundle with no public parameterless constructor ------------------
    public sealed class Por006Bundle : CliOptions
    {
        public Por006Bundle(int _) { }
        [CliOption("--page")] public int Page { get; set; }
    }
    public interface IPor006 { [CliRoute("go")] [CliCommandExample("go")] int Go(Por006Bundle b); }
    private sealed class Por006 : IPor006 { public int Go(Por006Bundle b) => 0; }

    // ---- POR008: a route method that cannot return an exit code -------------------------------
    public interface IPor008 { [CliRoute("go")] [CliCommandExample("go")] string Go(); }
    private sealed class Por008 : IPor008 { public string Go() => ""; }

    // ---- POR009: two options on one command declaring the same alias --------------------------
    public interface IPor009
    {
        [CliRoute("go")] [CliCommandExample("go")]
        int Go([CliOption("--name")] string a = "", [CliOption("--name")] string b = "");
    }
    private sealed class Por009 : IPor009 { public int Go(string a, string b) => 0; }

    // ---- POR010: an option type that cannot be built from a command-line string ---------------
    public sealed class Unconvertible { public Unconvertible(int _, int __) { } }
    public interface IPor010 { [CliRoute("go")] [CliCommandExample("go")] int Go([CliOption("--x")] Unconvertible? x = null); }
    private sealed class Por010 : IPor010 { public int Go(Unconvertible? x) => 0; }

    // ---- POR011: a route declaring the same placeholder twice ---------------------------------
    public interface IPor011 { [CliRoute("copy {path} {path}")] [CliCommandExample("copy a b")] int Copy(string path); }
    private sealed class Por011 : IPor011 { public int Copy(string path) => 0; }

    // ---- POR012: bool where CliFlag? was meant — EXEMPT ----------------------------------------
    public interface IPor012 { [CliRoute("go")] [CliCommandExample("go")] int Go([CliOption("--force")] bool force = false); }
    private sealed class Por012 : IPor012 { public int Go(bool force) => 0; }

    // ---- POR013: a catch clause swallowing CliExitException — EXEMPT ---------------------------
    public interface IPor013 { [CliRoute("go")] [CliCommandExample("go")] int Go(); }
    private sealed class Por013 : IPor013
    {
        public int Go()
        {
            try { throw new CliExitException("boom") { ExitCode = 3 }; }
            catch (Exception) { return 1; }
        }
    }

    /// <summary>Rules whose violation must be refused by <c>CliApplication.Create</c>.</summary>
    public static TheoryData<string, object> Backed => new()
    {
        { "POR001", new Por001() },
        { "POR002", new Por002() },
        { "POR003", new Por003() },
        { "POR005", new Por005() },
        { "POR006", new Por006() },
        { "POR008", new Por008() },
        { "POR009", new Por009() },
        { "POR010", new Por010() },
        { "POR011", new Por011() },
    };

    /// <summary>
    /// Rules with no runtime backstop, and the reason each one is right not to have one. Adding an
    /// entry here is a claim that there is nothing for the runtime to protect — not a note that the
    /// check is outstanding.
    /// </summary>
    public static TheoryData<string, object, string> Exempt => new()
    {
        {
            "POR004", new Por004(),
            // A missing example is an authoring-discipline failure, not a dispatch failure. The
            // command routes and binds correctly; what is missing is the executable documentation.
            // Refusing to start over it would turn a documentation gap into a production outage,
            // and unlike POR001/POR005/POR008 there is no misbehaviour for the runtime to prevent.
            "a route with no example dispatches correctly — there is nothing to protect at runtime"
        },
        {
            "POR012", new Por012(),
            // bool is a legitimate two-state value option. The rule is a Warning precisely because
            // no check can tell the intended use from the mistake, which is equally true at runtime.
            "bool is a legal two-state option; the rule diagnoses intent, which the runtime cannot read"
        },
        {
            "POR013", new Por013(),
            // The descriptor says so outright: no CLR mechanism makes a managed exception
            // uncatchable, and every ambient workaround guesses at whether the catch was deliberate.
            "no CLR mechanism makes an exception uncatchable; see the descriptor's remarks"
        },
    };

    [Theory]
    [MemberData(nameof(Backed))]
    public void Refuse_At_Create_What_The_Analyzer_Would_Have_Caught(string ruleId, object service)
    {
        var exception = Record.Exception(() => CliApplication.Create(cfg => cfg.AddCommands(service)));

        Assert.True(
            exception is CliConfigurationException,
            $"{ruleId} is classified as having a runtime backstop, but CliApplication.Create " +
            $"{(exception is null ? "accepted the violating contract" : $"threw {exception.GetType().Name}")}. " +
            "A user building without the analyzers gets no warning at all. Either add the check, or " +
            "move the rule to Exempt with the reason there is nothing to protect.");
    }

    [Theory]
    [MemberData(nameof(Exempt))]
    public void Accept_At_Create_What_Only_The_Analyzer_Can_Judge(string ruleId, object service, string why)
    {
        var exception = Record.Exception(() => CliApplication.Create(cfg => cfg.AddCommands(service)));

        Assert.True(
            exception is null,
            $"{ruleId} is classified as deliberately having no runtime backstop ({why}), but " +
            $"CliApplication.Create threw {exception?.GetType().Name}: {exception?.Message}. If a " +
            "backstop was added on purpose, move the rule to Backed and update CHARTER §6.5.");
    }

    /// <summary>
    /// The classification must be total. A rule that is in neither table has had no decision made
    /// about it, which is the state POR004 was in while the charter asserted the opposite.
    /// </summary>
    [Fact]
    public void Classify_Every_Live_Rule_As_Backed_Or_Exempt()
    {
        var classified = Backed.Select(row => (string)row[0])
            .Concat(Exempt.Select(row => (string)row[0]))
            .ToHashSet(StringComparer.Ordinal);

        var live = PorticoAnalyzerRules.LiveIds().ToHashSet(StringComparer.Ordinal);

        var unclassified = live.Except(classified).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var stale = classified.Except(live).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        Assert.True(unclassified.Length == 0,
            $"No runtime-backstop decision recorded for {string.Join(", ", unclassified)}. Add a " +
            "violating contract to Backed (and the check at CliApplication.Create), or to Exempt " +
            "with the reason the runtime has nothing to protect.");

        Assert.True(stale.Length == 0,
            $"{string.Join(", ", stale)} is classified here but no analyzer reports it. Remove the row.");
    }
}
