using System;
using Xunit;

namespace Portico;

// POR-70. A CLI is an HTTP API without the H (CHARTER §3), so a command's path must be readable off
// its route string in full — exactly as [Route("api/projects/{id}")] is in ASP.NET Core.
//
// Before POR-70 route POSITION could also come from a [CliArgument]'s position among the method's
// attributes (reordering two attribute lines moved a segment, and GetCustomAttributes guarantees no
// order) or from a parameter's ordinal (an argument appended to a route that never mentioned it).
// This suite pins that neither is possible any more.
//
// It replaces CliOrphanArgument_Should, whose subject — a method-level [CliArgument] naming no
// parameter — cannot be expressed now that the attribute is parameter-only.
// ReSharper disable once InconsistentNaming
public sealed class CliRouteOwnsPath_Should
{
    public interface IDescribedInSignatureOrder
    {
        [CliRoute("user {id} details")]
        [CliCommandExample("user 42 details")]
        int Details([CliArgument("which user")] string id);
    }

    public interface IDescribedWithOtherAttributesFirst
    {
        // Same route, same parameter — but every other attribute the framework reads is declared
        // in a different order. The route signature must be byte-identical.
        [CliCommandExample("user 42 details")]
        [System.ComponentModel.Description("show one user")]
        [CliRoute("user {id} details")]
        int Details([CliArgument("which user")] string id);
    }

    public sealed class InSignatureOrder : IDescribedInSignatureOrder
    {
        public int Details(string id) => 0;
    }

    public sealed class WithOtherAttributesFirst : IDescribedWithOtherAttributesFirst
    {
        public int Details(string id) => 0;
    }

    [Fact]
    public void Derive_The_Path_From_The_Route_String_And_Nothing_Else()
    {
        var a = CliApplication
            .Create(cfg => cfg.AddCommands(new InSignatureOrder()))
            .GetRouteSignatures();
        var b = CliApplication
            .Create(cfg => cfg.AddCommands(new WithOtherAttributesFirst()))
            .GetRouteSignatures();

        Assert.Equal(["user {id} details"], a);
        Assert.Equal(a, b);
    }

    public interface IReorderedParameters
    {
        // The route puts {b} first. The C# signature puts 'a' first. The route wins.
        [CliRoute("swap {b} {a}")]
        [CliCommandExample("swap B A")]
        int Swap([CliArgument("the a")] string a, [CliArgument("the b")] string b);
    }

    public sealed class ReorderedParameters : IReorderedParameters
    {
        public string? A { get; private set; }
        public string? B { get; private set; }

        public int Swap(string a, string b)
        {
            A = a;
            B = b;
            return 0;
        }
    }

    [Fact]
    public void Bind_Positionally_By_Route_Order_Not_Parameter_Order()
    {
        var svc = new ReorderedParameters();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, app.Run("app swap first second"));
        Assert.Equal("second", svc.A);   // 'a' is the SECOND route slot
        Assert.Equal("first", svc.B);    // 'b' is the FIRST
        Assert.Equal(["swap {b} {a}"], app.GetRouteSignatures());
    }

    public sealed class UnplacedArgService
    {
        // [CliArgument] describes a placeholder; it does not create one. The route says 'ship' and
        // nothing else, so 'target' has no position to bind to.
        [CliRoute("ship")]
        [CliCommandExample("ship prod")]
        public int Ship([CliArgument("where to ship")] string target) => 0;
    }

    [Fact]
    public void Reject_An_Argument_The_Route_Declares_No_Placeholder_For()
    {
        var ex = Assert.Throws<CliConfigurationException>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new UnplacedArgService())));

        Assert.Contains("parameter 'target'", ex.Message);
        Assert.Contains("""the route "ship" declares no {target} placeholder""", ex.Message);
        // The message must hand back the corrected route, not merely diagnose.
        Assert.Contains("""[CliRoute("ship {target}")]""", ex.Message);
    }

    public sealed class MultipleUnplacedArgService
    {
        [CliRoute("ship")]
        [CliCommandExample("ship prod eu")]
        public int Ship(
            [CliArgument("where to ship")] string target,
            [CliArgument("which region")] string region) => 0;
    }

    [Fact]
    public void Name_Every_Unplaced_Argument_In_One_Message()
    {
        var ex = Assert.Throws<CliConfigurationException>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new MultipleUnplacedArgService())));

        Assert.Contains("parameters 'target', 'region' carry [CliArgument]", ex.Message);
        Assert.Contains("declares no {target}, {region} placeholders", ex.Message);
        Assert.Contains("""[CliRoute("ship {target} {region}")]""", ex.Message);
    }

    public sealed class UndescribedPlaceholderService
    {
        public string? Seen { get; private set; }

        // A placeholder needs no [CliArgument] at all — the attribute is optional description.
        [CliRoute("ping {host}")]
        [CliCommandExample("ping example.com")]
        public int Ping(string host)
        {
            Seen = host;
            return 0;
        }
    }

    [Fact]
    public void Accept_A_Placeholder_With_No_CliArgument_At_All()
    {
        var svc = new UndescribedPlaceholderService();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, app.Run("app ping example.com"));
        Assert.Equal("example.com", svc.Seen);
    }
}
