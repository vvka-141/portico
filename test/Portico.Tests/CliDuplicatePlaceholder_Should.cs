using System;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-113. A route that repeats the same {placeholder} silently discards a value at dispatch
// and — worse — validates green through CliContractValidator. The runtime guard rejects the
// shape at CliApplication.Create; the analyzer (POR011) moves the failure into the edit loop.
public sealed class CliDuplicatePlaceholder_Should
{
    // --- Same placeholder twice in one method route ---

    public interface ICopyTool
    {
        [CliRoute("copy {p} {p}")]
        [CliCommandExample("copy a b")]
        int Copy(string p);
    }

    private sealed class CopyTool : ICopyTool
    {
        public int Copy(string p) => 0;
    }

    [Fact]
    public void Reject_A_Route_With_A_Repeated_Placeholder()
    {
        var ex = Assert.Throws<CliConfigurationException>(
            () => CliApplication.Create(cfg => cfg.AddCommands(new CopyTool())));

        Assert.Contains("{p}", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Copy", ex.Message, StringComparison.Ordinal);

        // The route is echoed as the author wrote it. This used to assert only Contains("copy"),
        // which passed while the message actually read `route "CliLiteralSegment { Text = copy }
        // CliArgumentSegment { Argument = p } {p}"` — the records' compiler-generated ToString,
        // leaking internal type names into a user's terminal. "copy" is a substring of that, so the
        // assertion could not tell the two apart.
        Assert.Contains("\"copy {p} {p}\"", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Segment {", ex.Message, StringComparison.Ordinal);
    }

    // --- Repeated placeholder across type-level prefix and method route (AC5) ---

    [CliRoute("db {id}")]
    public interface IDbTool
    {
        [CliRoute("get {id}")]
        [CliCommandExample("db 1 get 2")]
        int Get(string id);
    }

    private sealed class DbTool : IDbTool
    {
        public int Get(string id) => 0;
    }

    [Fact]
    public void Reject_A_Placeholder_Repeated_Across_Type_Prefix_And_Method_Route()
    {
        var ex = Assert.Throws<CliConfigurationException>(
            () => CliApplication.Create(cfg => cfg.AddCommands(new DbTool())));

        // The type prefix and the method route are one path by the time this is reported, so the
        // echoed route is the composed one — and it is still route syntax, not record shapes.
        Assert.Contains("\"db {id} get {id}\"", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Segment {", ex.Message, StringComparison.Ordinal);
    }

    // --- Contract validator no longer reports a false green (AC4) ---

    [Fact]
    public void Fail_The_Contract_Validator_Instead_Of_Passing_Silently()
    {
        Assert.Throws<CliConfigurationException>(
            () => new CliContractValidator<ICopyTool>().Enumerate());
    }

    // --- Distinct placeholders are fine ---

    public interface IMoveTool
    {
        [CliRoute("move {src} {dst}")]
        [CliCommandExample("move a b")]
        int Move(string src, string dst);
    }

    private sealed class MoveTool : IMoveTool
    {
        public int Move(string src, string dst) => 0;
    }

    [Fact]
    public void Accept_Distinct_Placeholders()
    {
        var app = CliApplication.Create(cfg => cfg.AddCommands(new MoveTool()));
        Assert.NotNull(app);
    }
}
