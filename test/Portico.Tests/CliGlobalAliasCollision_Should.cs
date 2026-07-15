using Xunit;

namespace Portico;

// POR-65. A route option that reuses a middleware-declared global alias (both declare -v) used to
// slip through RejectDuplicateOptionAliases, which only scanned the route's own options — so both
// silently bound the same token at dispatch. It must be rejected at CliApplication.Create, the same
// as a within-route duplicate.
// ReSharper disable once InconsistentNaming
public sealed class CliGlobalAliasCollision_Should
{
    public sealed class VerbosityMiddleware : CliMiddleware
    {
        [CliOption("--verbose|-v")]
        public CliFlag? Verbose { get; set; }
    }

    public sealed class CollidingService
    {
        // -v collides with the global; --value does not.
        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run([CliOption("--value|-v")] string? value = null) => 0;
    }

    public sealed class NonCollidingService
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run([CliOption("--value|-x")] string? value = null) => 0;
    }

    [Fact]
    public void Reject_A_Route_Option_That_Collides_With_A_Global_Middleware_Alias()
    {
        var ex = Assert.Throws<CliConfigurationException>(() => CliApplication.Create(cfg => cfg
            .UseMiddleware(new VerbosityMiddleware())
            .AddCommands(new CollidingService())));

        Assert.Contains("-v", ex.Message);
        Assert.Contains("global option", ex.Message);
    }

    [Fact]
    public void Accept_A_Route_Option_That_Does_Not_Collide_With_A_Global_Alias()
    {
        var app = CliApplication.Create(cfg => cfg
            .UseMiddleware(new VerbosityMiddleware())
            .AddCommands(new NonCollidingService()));

        Assert.Equal(0, app.Run("app.exe run -x hello"));
    }
}
