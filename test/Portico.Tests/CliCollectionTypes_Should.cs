using System;
using System.Collections;
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

    // --- Absent and optional binds EMPTY, not null (POR-150) -----------------------------------
    //
    // Operator decision, 2026-07-29. It used to bind null, so every handler taking an optional
    // collection paid a null check and forgetting it was an NRE at exit 1 inside USER code, where
    // Portico's diagnostics cannot help.
    //
    // The argument that settled it was not that null-checks are tedious. It is that a MAP option in
    // the same position has always bound an empty dictionary — CliDictionaryOptionMaterializer
    // builds its accumulator unconditionally — so two collection-shaped options in one signature
    // behaved differently for no reason a user could see. And argv has no syntax for an explicitly
    // empty list, so "absent" and "supplied with zero values" are indistinguishable at the terminal;
    // a distinction the CLI surface cannot express should not survive into the handler.

    /// <summary>
    /// One generic handler over the declared type, so this cannot pass for one shape and silently
    /// regress for another — the failure mode POR-144 was filed for.
    /// </summary>
    public sealed class AbsentProbe<T>
    {
        public object? Received;
        public bool Invoked;

        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run([CliOption("--tags")] T tags = default!)
        {
            Received = tags;
            Invoked = true;
            return 0;
        }
    }

    public static TheoryData<Type> OptionalCollectionShapes() => new()
    {
        typeof(string[]),
        typeof(List<string>),
        typeof(IEnumerable<string>),
        typeof(IReadOnlyList<string>),
        typeof(HashSet<string>),
        typeof(ISet<string>),
        typeof(SortedSet<string>),
        typeof(ImmutableArray<string>),
        typeof(ImmutableList<string>),
        typeof(ImmutableHashSet<string>),
        typeof(ImmutableSortedSet<string>),
    };

    [Theory]
    [MemberData(nameof(OptionalCollectionShapes))]
    public void Bind_An_Empty_Collection_When_Absent(Type declaredType)
    {
        var probeType = typeof(AbsentProbe<>).MakeGenericType(declaredType);
        object? probe = null;

        CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(
                probeType,
                () => probe = Activator.CreateInstance(probeType)!,
                []))
            .Run("app run")
            .ExpectExit(0);

        Assert.True((bool)probeType.GetField(nameof(AbsentProbe<int>.Invoked))!.GetValue(probe)!);

        var received = probeType.GetField(nameof(AbsentProbe<int>.Received))!.GetValue(probe);

        Assert.True(received is not null, $"'{declaredType.Name}' bound null when absent.");
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable>(received));
    }

    /// <summary>
    /// The change is confined to the absent case: supplying values still binds them.
    /// </summary>
    [Fact]
    public void Still_Bind_Supplied_Values_After_The_Empty_Default()
    {
        var svc = new ListService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --envs a b").ExpectExit(0);

        Assert.Equal(new[] { "a", "b" }, svc.Received);
    }

    /// <summary>
    /// A collection with no default and no <c>?</c> is <b>required</b>, not optional — so it never
    /// reaches the absent-and-optional branch and still errors. This is also why the ticket's
    /// proposed "empty for non-nullable, null for nullable" rule could not work: the two facts it
    /// wanted to discriminate on are the same two facts that make a collection optional at all.
    /// </summary>
    [Fact]
    public void Still_Reject_A_Required_Collection_That_Is_Missing()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new IntSetService()))
            .Run("app run");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("required option", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The empty-collection default replaces <b>only</b> the <c>null</c> that C# forced on the
    /// author — a parameter default must be a compile-time constant, and <c>null</c> is the only one
    /// a collection type can express. A non-null default is passed through untouched.
    /// </summary>
    /// <remarks>
    /// The only other route to a default is <c>[CliOption(DefaultValue = "…")]</c>, and on a
    /// collection <b>that is broken</b>: the string is converted through the ELEMENT converter, so a
    /// <c>string</c> reaches a <c>string[]</c> parameter and dies at <c>MethodInfo.Invoke</c> with
    /// <c>exit 1</c>. Pre-existing, found while implementing POR-150, filed as <b>POR-156</b>.
    /// <para>
    /// This test asserts that failure still happens, which is the point: POR-150 must not quietly
    /// swallow a non-null default and substitute an empty collection, because that would convert a
    /// visible bug into a silent wrong value. When POR-156 lands this test goes red and should be
    /// rewritten to assert the corrected binding.
    /// </para>
    /// </remarks>
    [Fact]
    public void Pass_A_Non_Null_Default_Through_Untouched()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new AttributeDefaultService()))
            .Run("app run");

        Assert.Equal(CliExitException.RuntimeErrorExitCode, result.ExitCode);
        Assert.Contains("cannot be converted", result.StandardError, StringComparison.Ordinal);
    }

    public sealed class AttributeDefaultService
    {
        public string[]? Received;

        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run([CliOption("--regions", DefaultValue = "eu,us")] string[]? regions = null)
        {
            Received = regions;
            return 0;
        }
    }
}
