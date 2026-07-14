using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// Every collection shape the framework promises to support on an option parameter, pinned
// via a round-trip through the test harness. When a future refactor accidentally drops a
// type from SupportedCollectionDefinitions or breaks the factory dispatch, one of these
// tests fires immediately.
public sealed class CliCollectionTypes_Should
{
    // --- List-like ---------------------------------------------------------------------------

    public sealed class ArrayService
    {
        public string[]? Received;

        [CliRoute("run")]
        [CliCommandExample("run --envs dev prod")]
        public int Run([CliOption("--envs")] string[] envs)
        {
            Received = envs;
            return 0;
        }
    }

    [Fact]
    public void Materialize_Array()
    {
        var svc = new ArrayService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --envs dev staging prod").ExpectExit(0);
        Assert.Equal(new[] { "dev", "staging", "prod" }, svc.Received);
    }

    public sealed class ListService
    {
        public List<string>? Received;

        [CliRoute("run")]
        [CliCommandExample("run --envs dev prod")]
        public int Run([CliOption("--envs")] List<string> envs)
        {
            Received = envs;
            return 0;
        }
    }

    [Fact]
    public void Materialize_List()
    {
        var svc = new ListService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --envs a b c").ExpectExit(0);
        Assert.Equal(new[] { "a", "b", "c" }, svc.Received);
    }

    public sealed class ImmutableArrayService
    {
        public ImmutableArray<string> Received;

        [CliRoute("run")]
        [CliCommandExample("run --envs dev prod")]
        public int Run([CliOption("--envs")] ImmutableArray<string> envs)
        {
            Received = envs;
            return 0;
        }
    }

    [Fact]
    public void Materialize_ImmutableArray()
    {
        var svc = new ImmutableArrayService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --envs a b c").ExpectExit(0);
        Assert.Equal(new[] { "a", "b", "c" }, svc.Received.ToArray());
    }

    public sealed class ImmutableListService
    {
        public ImmutableList<string>? Received;

        [CliRoute("run")]
        [CliCommandExample("run --envs dev prod")]
        public int Run([CliOption("--envs")] ImmutableList<string> envs)
        {
            Received = envs;
            return 0;
        }
    }

    [Fact]
    public void Materialize_ImmutableList()
    {
        var svc = new ImmutableListService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --envs a b c").ExpectExit(0);
        Assert.Equal(new[] { "a", "b", "c" }, svc.Received!.ToArray());
    }

    public sealed class IImmutableListService
    {
        public IImmutableList<string>? Received;

        [CliRoute("run")]
        [CliCommandExample("run --envs dev prod")]
        public int Run([CliOption("--envs")] IImmutableList<string> envs)
        {
            Received = envs;
            return 0;
        }
    }

    [Fact]
    public void Materialize_IImmutableList()
    {
        var svc = new IImmutableListService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --envs a b c").ExpectExit(0);
        Assert.Equal(new[] { "a", "b", "c" }, svc.Received!.ToArray());
    }

    // --- Set-like ----------------------------------------------------------------------------

    public sealed class HashSetService
    {
        public HashSet<string>? Received;

        [CliRoute("run")]
        [CliCommandExample("run --tags x y")]
        public int Run([CliOption("--tags")] HashSet<string> tags)
        {
            Received = tags;
            return 0;
        }
    }

    [Fact]
    public void Materialize_HashSet_And_Dedupe()
    {
        var svc = new HashSetService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --tags a b a c b").ExpectExit(0);
        Assert.Equal(new[] { "a", "b", "c" }, svc.Received!.OrderBy(x => x).ToArray());
    }

    public sealed class SortedSetService
    {
        public SortedSet<string>? Received;

        [CliRoute("run")]
        [CliCommandExample("run --tags z a m")]
        public int Run([CliOption("--tags")] SortedSet<string> tags)
        {
            Received = tags;
            return 0;
        }
    }

    [Fact]
    public void Materialize_SortedSet_Sorted_And_Deduped()
    {
        var svc = new SortedSetService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --tags z m a m").ExpectExit(0);
        Assert.Equal(new[] { "a", "m", "z" }, svc.Received!.ToArray());
    }

    public sealed class ISetService
    {
        public ISet<string>? Received;

        [CliRoute("run")]
        [CliCommandExample("run --tags x y")]
        public int Run([CliOption("--tags")] ISet<string> tags)
        {
            Received = tags;
            return 0;
        }
    }

    [Fact]
    public void Materialize_ISet_Via_HashSet()
    {
        var svc = new ISetService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --tags a b a").ExpectExit(0);
        Assert.IsAssignableFrom<HashSet<string>>(svc.Received);
        Assert.Equal(new[] { "a", "b" }, svc.Received!.OrderBy(x => x).ToArray());
    }

    public sealed class IReadOnlySetService
    {
        public IReadOnlySet<string>? Received;

        [CliRoute("run")]
        [CliCommandExample("run --tags x y")]
        public int Run([CliOption("--tags")] IReadOnlySet<string> tags)
        {
            Received = tags;
            return 0;
        }
    }

    [Fact]
    public void Materialize_IReadOnlySet_Via_HashSet()
    {
        var svc = new IReadOnlySetService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --tags a b a").ExpectExit(0);
        Assert.Equal(new[] { "a", "b" }, svc.Received!.OrderBy(x => x).ToArray());
    }

    public sealed class ImmutableHashSetService
    {
        public ImmutableHashSet<string>? Received;

        [CliRoute("run")]
        [CliCommandExample("run --tags x y")]
        public int Run([CliOption("--tags")] ImmutableHashSet<string> tags)
        {
            Received = tags;
            return 0;
        }
    }

    [Fact]
    public void Materialize_ImmutableHashSet()
    {
        var svc = new ImmutableHashSetService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --tags a b a c").ExpectExit(0);
        Assert.Equal(new[] { "a", "b", "c" }, svc.Received!.OrderBy(x => x).ToArray());
    }

    public sealed class IImmutableSetService
    {
        public IImmutableSet<string>? Received;

        [CliRoute("run")]
        [CliCommandExample("run --tags x y")]
        public int Run([CliOption("--tags")] IImmutableSet<string> tags)
        {
            Received = tags;
            return 0;
        }
    }

    [Fact]
    public void Materialize_IImmutableSet()
    {
        var svc = new IImmutableSetService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --tags a b a c").ExpectExit(0);
        Assert.Equal(new[] { "a", "b", "c" }, svc.Received!.OrderBy(x => x).ToArray());
    }

    public sealed class ImmutableSortedSetService
    {
        public ImmutableSortedSet<string>? Received;

        [CliRoute("run")]
        [CliCommandExample("run --tags z a m")]
        public int Run([CliOption("--tags")] ImmutableSortedSet<string> tags)
        {
            Received = tags;
            return 0;
        }
    }

    [Fact]
    public void Materialize_ImmutableSortedSet_Sorted_And_Deduped()
    {
        var svc = new ImmutableSortedSetService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --tags z m a m").ExpectExit(0);
        Assert.Equal(new[] { "a", "m", "z" }, svc.Received!.ToArray());
    }

    // --- Numeric item type (exercises type-converter + set dedup together) --------------------

    public sealed class IntSetService
    {
        public HashSet<int>? Received;

        [CliRoute("run")]
        [CliCommandExample("run --ports 80 443")]
        public int Run([CliOption("--ports")] HashSet<int> ports)
        {
            Received = ports;
            return 0;
        }
    }

    [Fact]
    public void Materialize_HashSet_Of_Int()
    {
        var svc = new IntSetService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --ports 80 443 80 8080").ExpectExit(0);
        Assert.Equal(new[] { 80, 443, 8080 }, svc.Received!.OrderBy(x => x).ToArray());
    }
}
