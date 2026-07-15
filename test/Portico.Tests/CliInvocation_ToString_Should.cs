using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class CliInvocation_ToString_Should
{
    // A single command line exercising all six option-capture shapes, in parse order:
    //   flag, scalar, collection, keyed-value, keyed-flag, keyed-collection.
    private const string AllSixShapes =
        "app --verbose --out result.txt --files a.txt b.txt c.txt " +
        "--config[env] prod --feature[dark] --envs[region] us-east us-west";

    /// <summary>
    /// The readable round-trip form ("G") must render every one of the six capture shapes.
    /// Before POR-57, plain collections vanished (their capture type did not implement
    /// <see cref="ICliCollectionCapture"/>) and keyed flags / keyed collections vanished
    /// (the branch enumerated the concrete <see cref="CliKeyValueOptionCapture"/> only).
    /// </summary>
    [Fact]
    public void Render_All_Six_Capture_Shapes_In_General_Format()
    {
        var invocation = CliInvocation.FromArgs(AllSixShapes);

        Assert.Equal(
            "app --verbose --out result.txt --files a.txt b.txt c.txt " +
            "--config[env] prod --feature[dark] --envs[region] us-east us-west",
            invocation.ToString("G"));

        // The parameterless ToString() is the same "G" form and is what CliTimingMiddleware
        // and any $"{invocation}" logging call renders.
        Assert.Equal(invocation.ToString("G"), invocation.ToString());
    }

    /// <summary>
    /// The debug form ("D") collapses collections to <c>name ..</c> and maps to <c>name[..] ..</c>,
    /// but must still enumerate all six shapes.
    /// </summary>
    [Fact]
    public void Render_All_Six_Capture_Shapes_In_Debug_Format()
    {
        var invocation = CliInvocation.FromArgs(AllSixShapes);

        Assert.Equal(
            "app --verbose --out .. --files .. --config[..] .. --feature[..] .. --envs[..] ..",
            invocation.ToString("D"));
    }

    /// <summary>Plain multi-value collections round-trip in the "G" form (the primary POR-57 regression).</summary>
    [Fact]
    public void Render_Plain_Collection_In_General_Format()
    {
        var invocation = CliInvocation.FromArgs("app --files a.txt b.txt c.txt");

        Assert.Equal("app --files a.txt b.txt c.txt", invocation.ToString("G"));
    }

    /// <summary>A keyed flag renders its bracket key with no value in the "G" form.</summary>
    [Fact]
    public void Render_Keyed_Flag_In_General_Format()
    {
        var invocation = CliInvocation.FromArgs("app --feature[dark]");

        Assert.Equal("app --feature[dark]", invocation.ToString("G"));
    }

    /// <summary>A keyed collection renders its bracket key and all values in the "G" form.</summary>
    [Fact]
    public void Render_Keyed_Collection_In_General_Format()
    {
        var invocation = CliInvocation.FromArgs("app --envs[region] us-east us-west eu-west");

        Assert.Equal("app --envs[region] us-east us-west eu-west", invocation.ToString("G"));
    }

    /// <summary>A value containing whitespace is quoted in both scalar and keyed-value renders.</summary>
    [Fact]
    public void Quote_Values_With_Whitespace()
    {
        var invocation = CliInvocation.FromArgs("app --out \"my file.txt\" --config[path] \"c:\\program files\"");

        Assert.Equal("app --out \"my file.txt\" --config[path] \"c:\\program files\"", invocation.ToString("G"));
    }
}
