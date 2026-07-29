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
        // The nullable form of the one struct shape (POR-157). It binds empty when absent like every
        // other entry — the `?` is how C# spells "optional" for a struct, not a request for null.
        typeof(ImmutableArray<string>?),
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
    /// a collection type can express. A default the author actually supplied is bound, not replaced.
    /// </summary>
    /// <remarks>
    /// Rewritten when POR-156 landed, exactly as this test's previous form said it should be. It
    /// used to assert that <c>[CliOption(DefaultValue = "…")]</c> on a collection <em>failed</em>,
    /// because it did — the string was converted through the element converter and died at
    /// <c>MethodInfo.Invoke</c>. The point then was that POR-150 must not swallow a non-null default
    /// and substitute an empty collection, turning a visible bug into a silent wrong value. The point
    /// now is the same invariant with the bug gone: the author's default wins over the empty default.
    /// </remarks>
    [Fact]
    public void Bind_A_Default_The_Author_Supplied_Rather_Than_An_Empty_One()
    {
        var svc = new AttributeDefaultService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run").ExpectExit(0);

        Assert.Equal(new[] { "eu", "us" }, svc.Received);
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

    // --- A nullable struct collection is the same collection (POR-157) -------------------------
    //
    // ImmutableArray<T> is a struct, so ImmutableArray<T>? is the only way to write an OPTIONAL
    // immutable-array option that reads as optional at the declaration site. It was refused at
    // startup with "Option '--tags' has type 'Nullable`1'", telling the user to put a [TypeConverter]
    // on a BCL generic they cannot modify.
    //
    // What makes this a plain bug rather than a design question is that the rest of the pipeline had
    // already answered it: CliOptionAttribute.CanAccept unwraps nullables (POR-37, TimeSpan?), and so
    // does the POR010 analyzer, which therefore never flagged the shape it was being blamed for.
    // Only the materializer's shape detection disagreed.

    public sealed class NullableImmutableArrayService
    {
        public ImmutableArray<string>? Received;

        [CliRoute("run")]
        [CliCommandExample("run --tags a b")]
        public int Run([CliOption("--tags")] ImmutableArray<string>? tags = null)
        {
            Received = tags;
            return 0;
        }
    }

    /// <summary>
    /// Values bind through to the handler. The materializer builds the <em>underlying</em> shape and
    /// hands <c>MethodInfo.Invoke</c> a boxed <c>ImmutableArray&lt;string&gt;</c>, which the runtime
    /// binds to a <c>Nullable&lt;T&gt;</c> parameter — so detection and construction have to agree
    /// about the unwrap, or the shape is recognised and then cannot be built.
    /// </summary>
    [Fact]
    public void Bind_A_Nullable_Struct_Collection()
    {
        var svc = new NullableImmutableArrayService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc))
            .Run("app run --tags a b").ExpectExit(0);

        Assert.True(svc.Received.HasValue);
        Assert.Equal(new[] { "a", "b" }, svc.Received!.Value.ToArray());
    }

    /// <summary>
    /// Absent binds an empty array, not <c>null</c> — POR-150's rule, applied to the nullable form
    /// too. A shape that bound empty when written <c>ImmutableArray&lt;string&gt;</c> and null when
    /// written <c>ImmutableArray&lt;string&gt;?</c> would make the <c>?</c> mean something no other
    /// collection shape lets it mean.
    /// </summary>
    [Fact]
    public void Bind_An_Empty_Nullable_Struct_Collection_When_Absent()
    {
        var svc = new NullableImmutableArrayService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run").ExpectExit(0);

        Assert.True(svc.Received.HasValue);
        Assert.Empty(svc.Received!.Value);
    }

    /// <summary>
    /// The nullable form reaches every other collection feature too — here the comma-split attribute
    /// default from POR-156, which resolves through <c>CliOptionDefaultResolver</c> rather than the
    /// materializer. The unwrap lives in the shared detection helpers precisely so features do not
    /// have to be re-fixed one path at a time.
    /// </summary>
    [Fact]
    public void Apply_A_Comma_Split_Default_To_The_Nullable_Form()
    {
        var svc = new NullableDefaultService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run").ExpectExit(0);

        Assert.Equal(new[] { "eu", "us" }, svc.Received!.Value.ToArray());
    }

    public sealed class NullableDefaultService
    {
        public ImmutableArray<string>? Received;

        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run([CliOption("--regions", DefaultValue = "eu,us")] ImmutableArray<string>? regions = null)
        {
            Received = regions;
            return 0;
        }
    }
}
