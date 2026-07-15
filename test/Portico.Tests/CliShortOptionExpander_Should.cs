using System.Collections.Generic;
using Xunit;

namespace Portico;

// Direct unit tests for the POSIX short-option preprocessor. The map-arity cases pin POR-58:
// a short map option (-e[region]) must never be speculatively split, or its bracket key is torn
// off before the tokenizer sees it.
// ReSharper disable once InconsistentNaming
public sealed class CliShortOptionExpander_Should
{
    private static CliShortOptionSchema Schema(params (char c, CliShortOptionArity arity)[] entries)
    {
        var map = new Dictionary<char, CliShortOptionArity>();
        foreach (var (c, arity) in entries) map[c] = arity;
        return new CliShortOptionSchema(map);
    }

    private static IReadOnlySet<string> Names(params string[] names) => new HashSet<string>(names);

    [Fact]
    public void Leave_A_Short_Map_Token_Unsplit()
    {
        var schema = Schema(('e', CliShortOptionArity.Map));

        var result = CliShortOptionExpander.Expand(["-e[region]", "eu"], schema, Names("--env", "-e"));

        // Unchanged: the [region] suffix must reach the tokenizer intact.
        Assert.Equal(["-e[region]", "eu"], result);
    }

    [Fact]
    public void Leave_A_Short_Map_Token_Unsplit_When_It_Trails_A_Flag_Cluster()
    {
        var schema = Schema(('v', CliShortOptionArity.Flag), ('e', CliShortOptionArity.Map));

        var result = CliShortOptionExpander.Expand(["-ve[region]"], schema, Names("-v", "-e"));

        // A map short mid-cluster carries a key we cannot represent in a split → leave it whole.
        Assert.Equal(["-ve[region]"], result);
    }

    [Fact]
    public void Still_Split_A_Scalar_Short_With_A_Glued_Value()
    {
        var schema = Schema(('n', CliShortOptionArity.Scalar));

        var result = CliShortOptionExpander.Expand(["-n5"], schema, Names("-n"));

        Assert.Equal(["-n", "5"], result);
    }

    [Fact]
    public void Still_Split_A_Flag_Cluster()
    {
        var schema = Schema(('a', CliShortOptionArity.Flag), ('b', CliShortOptionArity.Flag));

        var result = CliShortOptionExpander.Expand(["-ab"], schema, Names("-a", "-b"));

        Assert.Equal(["-a", "-b"], result);
    }
}
