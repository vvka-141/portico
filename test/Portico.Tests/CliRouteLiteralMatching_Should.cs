using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// Route literals match as exact, case-insensitive tokens — not regex patterns (SOL-83). Before the
// fix, a literal was compiled into `^(?:{s})$` (interpreted, per dispatch), so a segment containing a
// regex metacharacter matched as a pattern. These pin the intended string-equality semantics.
public sealed class CliRouteLiteralMatching_Should
{
    public sealed class VersionedRouteService
    {
        public bool Invoked;

        // The '.' is a literal here, not a regex "any char".
        [CliRoute("show v2.0")]
        [CliCommandExample("show v2.0")]
        public int Show()
        {
            Invoked = true;
            return 0;
        }
    }

    [Fact]
    public void Match_A_Literal_Segment_Exactly()
    {
        var svc = new VersionedRouteService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app show v2.0").ExpectExit(0);
        Assert.True(svc.Invoked);
    }

    [Fact]
    public void Match_A_Literal_Segment_Case_Insensitively()
    {
        var svc = new VersionedRouteService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app SHOW V2.0").ExpectExit(0);
        Assert.True(svc.Invoked);
    }

    [Fact]
    public void Not_Treat_A_Metacharacter_In_A_Literal_As_A_Pattern()
    {
        var svc = new VersionedRouteService();
        // 'v2X0' would match the old `v2.0` regex (the '.' meant any char); it must not now.
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app show v2X0");
        Assert.False(svc.Invoked);
    }
}
