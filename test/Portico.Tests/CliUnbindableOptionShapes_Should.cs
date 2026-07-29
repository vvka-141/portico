using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using Portico.Reflection;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
/// <summary>
/// The negative half of <see cref="CliCollectionTypes_Should"/>.
///
/// That suite has one test per entry in the supported-shapes allow-list and every one passes — so it
/// proves the allow-list works and never asks what happens <em>outside</em> it. POR-144 lived in that
/// blind spot: acceptance and construction were decided in two places that could disagree, and eight
/// collection- or map-shaped declared types silently fell through to the scalar materializer. They
/// bound a value of the wrong type and died inside <c>MethodInfo.Invoke</c> at exit 1 with a raw BCL
/// type name — not a configuration error, not a usage error, not a POR010 compile error.
///
/// The load-bearing test here is <see cref="Bind_or_refuse_every_collection_shaped_type"/>. It does
/// not assert which bucket a shape lands in; it asserts that the third bucket does not exist. Adding
/// a shape to <see cref="CollectionShapedTypes"/> costs one line and requires no decision, so this
/// cannot drift the way a per-shape list did.
/// </summary>
public sealed class CliUnbindableOptionShapes_Should
{
    /// <summary>
    /// One generic handler, closed over the declared type under test. Registering it through
    /// <c>AddCommands(Type, Func&lt;object&gt;, …)</c> is what lets the corpus be data rather than
    /// a hand-written class per shape.
    /// </summary>
    public sealed class Probe<T>
    {
        public object? Received;

        [CliRoute("run")]
        [CliCommandExample("run --v x")]
        public int Run([CliOption("--v")] T value)
        {
            Received = value;
            return 0;
        }
    }

    /// <summary>
    /// Every collection- or map-shaped type worth asking about, with the command line that fills it.
    /// Supported and unsupported shapes deliberately share one corpus — the invariant is the same
    /// for both, and keeping them apart is what would let a shape quietly change bucket.
    /// </summary>
    public static TheoryData<Type, string> CollectionShapedTypes() => new()
    {
        // List-like
        { typeof(string[]), "app run --v a b" },
        { typeof(List<string>), "app run --v a b" },
        { typeof(IEnumerable<string>), "app run --v a b" },
        { typeof(IList<string>), "app run --v a b" },
        { typeof(ICollection<string>), "app run --v a b" },
        { typeof(IReadOnlyList<string>), "app run --v a b" },
        { typeof(IReadOnlyCollection<string>), "app run --v a b" },
        { typeof(Collection<string>), "app run --v a b" },
        { typeof(Queue<string>), "app run --v a b" },
        { typeof(Stack<string>), "app run --v a b" },
        { typeof(LinkedList<string>), "app run --v a b" },
        // Set-like
        { typeof(HashSet<string>), "app run --v a b" },
        { typeof(SortedSet<string>), "app run --v a b" },
        { typeof(ISet<string>), "app run --v a b" },
        { typeof(IReadOnlySet<string>), "app run --v a b" },
        // Immutable
        { typeof(ImmutableArray<string>), "app run --v a b" },
        { typeof(ImmutableList<string>), "app run --v a b" },
        { typeof(IImmutableList<string>), "app run --v a b" },
        { typeof(ImmutableHashSet<string>), "app run --v a b" },
        { typeof(IImmutableSet<string>), "app run --v a b" },
        { typeof(ImmutableSortedSet<string>), "app run --v a b" },
        { typeof(ImmutableQueue<string>), "app run --v a b" },
        { typeof(ImmutableStack<string>), "app run --v a b" },
        // Maps
        { typeof(Dictionary<string, string>), "app run --v[k] a" },
        { typeof(IDictionary<string, string>), "app run --v[k] a" },
        { typeof(IReadOnlyDictionary<string, string>), "app run --v[k] a" },
        { typeof(SortedDictionary<string, string>), "app run --v[k] a" },
        { typeof(SortedList<string, string>), "app run --v[k] a" },
        { typeof(ConcurrentDictionary<string, string>), "app run --v[k] a" },
        { typeof(ImmutableDictionary<string, string>), "app run --v[k] a" },
        { typeof(IImmutableDictionary<string, string>), "app run --v[k] a" },
        { typeof(ImmutableSortedDictionary<string, string>), "app run --v[k] a" },
        // A map whose key is not string, and a collection whose element has no converter.
        { typeof(Dictionary<int, string>), "app run --v[1] a" },
        { typeof(List<CliUnbindableOptionShapes_Should>), "app run --v a" },
    };

    /// <summary>
    /// The invariant, and the whole point of this file: a collection- or map-shaped option either
    /// binds a value its own parameter accepts, or is refused at <c>Create</c> with a
    /// <see cref="CliConfigurationException"/>. There is no third outcome — no exit 1, no value of
    /// the wrong runtime type, no usage error blaming the user for a declaration the framework
    /// cannot honour.
    /// </summary>
    [Theory]
    [MemberData(nameof(CollectionShapedTypes))]
    public void Bind_or_refuse_every_collection_shaped_type(Type declaredType, string commandLine)
    {
        var probeType = typeof(Probe<>).MakeGenericType(declaredType);

        object? probe = null;
        int exitCode;
        try
        {
            var result = CliTestHarness
                .ForApplication(cfg => cfg.AddCommands(
                    probeType,
                    () => probe = Activator.CreateInstance(probeType)!,
                    []))
                .Run(commandLine);
            exitCode = result.ExitCode;
        }
        catch (CliConfigurationException)
        {
            // Refused at startup, naming the option and the shapes that work. Acceptable outcome.
            return;
        }

        Assert.True(exitCode == 0,
            $"'{FriendlyName(declaredType)}' was neither refused at Create nor bound: the app exited {exitCode}. " +
            "A shape the framework cannot construct must be a CliConfigurationException at startup, " +
            "never a runtime failure (POR-144).");

        var received = probeType.GetField(nameof(Probe<int>.Received))!.GetValue(probe);
        Assert.True(received is not null,
            $"'{FriendlyName(declaredType)}' dispatched but bound nothing.");
        Assert.True(declaredType.IsInstanceOfType(received),
            $"'{FriendlyName(declaredType)}' bound a '{FriendlyName(received!.GetType())}'. That mismatch is " +
            "invisible until MethodInfo.Invoke rejects it at exit 1 — the exact POR-144 failure.");
    }

    /// <summary>
    /// The four map shapes POR-144 named, each binding to its own declared type rather than to the
    /// <c>Dictionary&lt;string,V&gt;</c> accumulator the materializer builds internally.
    /// </summary>
    [Fact]
    public void Bind_the_dictionary_shapes_that_used_to_fail_at_dispatch()
    {
        Assert.IsType<SortedDictionary<string, int>>(BindMap<SortedDictionary<string, int>>());
        Assert.IsAssignableFrom<ImmutableDictionary<string, int>>(BindMap<ImmutableDictionary<string, int>>());
        Assert.IsAssignableFrom<IDictionary<string, int>>(BindMap<IDictionary<string, int>>());
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, int>>(BindMap<IReadOnlyDictionary<string, int>>());
    }

    /// <summary>
    /// Values survive the conversion to the declared shape — a converter step that dropped or
    /// reordered entries would still satisfy the type assertions above.
    /// </summary>
    [Fact]
    public void Preserve_every_entry_through_the_shape_conversion()
    {
        var sorted = (SortedDictionary<string, int>)BindMap<SortedDictionary<string, int>>("app run --v[b] 2 --v[a] 1")!;
        Assert.Equal(new[] { "a", "b" }, sorted.Keys);
        Assert.Equal(new[] { 1, 2 }, sorted.Values);

        var immutable = (ImmutableDictionary<string, int>)BindMap<ImmutableDictionary<string, int>>("app run --v[b] 2 --v[a] 1")!;
        Assert.Equal(2, immutable.Count);
        Assert.Equal(1, immutable["a"]);
        Assert.Equal(2, immutable["b"]);
    }

    /// <summary>
    /// An interface parameter is not a special case that happens to work — a
    /// <c>Dictionary&lt;string,V&gt;</c> satisfies both dictionary interfaces, so the detection
    /// helper has to recognise <c>IDictionary&lt;,&gt;</c> and <c>IReadOnlyDictionary&lt;,&gt;</c>
    /// as separate hierarchies. Fixing only the first leaves the more idiomatic one broken.
    /// </summary>
    [Fact]
    public void Treat_the_two_dictionary_interfaces_as_separate_hierarchies()
    {
        Assert.DoesNotContain(
            typeof(IReadOnlyDictionary<string, string>).GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));

        Assert.Single(typeof(IDictionary<string, string>).GetGenericDictionaryArgumentTypes());
        Assert.Single(typeof(IReadOnlyDictionary<string, string>).GetGenericDictionaryArgumentTypes());

        // A concrete dictionary satisfies both interfaces over the same arguments. One map, one
        // answer — a duplicate here would build two materializers for one parameter.
        Assert.Single(typeof(Dictionary<string, string>).GetGenericDictionaryArgumentTypes());
    }

    /// <summary>
    /// A refusal has to be actionable: it names the option, spells the declared type the way the
    /// user wrote it (not <c>Queue`1</c>), and lists shapes that work.
    /// </summary>
    [Fact]
    public void Explain_a_refused_shape_in_terms_the_user_can_act_on()
    {
        var collection = Assert.Throws<CliConfigurationException>(() =>
            CliTestHarness.ForApplication(cfg => cfg.AddCommands(new QueueService())).Run("app run --v a"));

        Assert.Contains("--v", collection.Message);
        Assert.Contains("Queue<string>", collection.Message);
        Assert.DoesNotContain("`1", collection.Message);
        Assert.Contains("List<string>", collection.Message);

        var map = Assert.Throws<CliConfigurationException>(() =>
            CliTestHarness.ForApplication(cfg => cfg.AddCommands(new SortedListService())).Run("app run --v[k] a"));

        Assert.Contains("--v", map.Message);
        Assert.Contains("SortedList<string, string>", map.Message);
        Assert.Contains("Dictionary<string,V>", map.Message);
    }

    /// <summary>
    /// A map key that is not string is a distinct diagnosis from an unconvertible value type, and it
    /// used to be reported as neither — the cascade fell through and the scalar terminal blamed the
    /// whole dictionary for lacking a <c>[TypeConverter]</c>.
    /// </summary>
    [Fact]
    public void Name_the_key_type_when_a_map_key_is_not_string()
    {
        var ex = Assert.Throws<CliConfigurationException>(() =>
            CliTestHarness.ForApplication(cfg => cfg.AddCommands(new IntKeyService())).Run("app run --v[1] a"));

        Assert.Contains("--v", ex.Message);
        Assert.Contains("must be string", ex.Message);
    }

    /// <summary>
    /// The refusal is aimed at shapes the framework cannot construct, not at every type that happens
    /// to be enumerable. A user type carrying its own <see cref="System.ComponentModel.TypeConverter"/>
    /// still binds as a scalar, which is what the declaration means.
    /// </summary>
    [Fact]
    public void Leave_a_string_convertible_type_on_the_scalar_path()
    {
        var svc = new TagListService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run --v a,b,c").ExpectExit(0);
        Assert.Equal(["a", "b", "c"], svc.Received!.Items);
    }

    /// <summary>
    /// The attribute's key comparer has to reach the shape that is finally handed to the handler.
    /// An immutable dictionary carries its own comparer, so building one without it would make
    /// <c>--v[A]</c> and <c>--v[a]</c> a duplicate at parse time under an OrdinalIgnoreCase
    /// override and then a miss on lookup — the same value answered two ways.
    /// </summary>
    [Fact]
    public void Carry_the_key_comparer_into_the_converted_shape()
    {
        var svc = new CaseInsensitiveImmutableService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run --v[Region] eu").ExpectExit(0);

        Assert.Equal("eu", svc.Received!["region"]);
        Assert.Equal("eu", svc.Received!["REGION"]);

        // And the accumulator's duplicate detection agrees with it.
        var duplicate = new CaseInsensitiveImmutableService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(duplicate))
            .Run("app run --v[Region] eu --v[region] us")
            .ExpectExit(2);
    }

    public sealed class CaseInsensitiveKeysAttribute : CliOptionAttribute
    {
        public CaseInsensitiveKeysAttribute(string specification) : base(specification) { }

        public override StringComparer GetValueComparer() => StringComparer.OrdinalIgnoreCase;
    }

    public sealed class CaseInsensitiveImmutableService
    {
        public ImmutableDictionary<string, string>? Received;

        [CliRoute("run")]
        [CliCommandExample("run --v[Region] eu")]
        public int Run([CaseInsensitiveKeys("--v")] ImmutableDictionary<string, string> value)
        {
            Received = value;
            return 0;
        }
    }

    private static object? BindMap<T>(string commandLine = "app run --v[a] 1")
    {
        var probe = new Probe<T>();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(probe)).Run(commandLine).ExpectExit(0);
        return probe.Received;
    }

    private static string FriendlyName(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(a => a.Name))}>"
            : type.Name;

    public sealed class QueueService
    {
        [CliRoute("run")]
        [CliCommandExample("run --v a")]
        public int Run([CliOption("--v")] Queue<string> value) => 0;
    }

    public sealed class SortedListService
    {
        [CliRoute("run")]
        [CliCommandExample("run --v[k] a")]
        public int Run([CliOption("--v")] SortedList<string, string> value) => 0;
    }

    public sealed class IntKeyService
    {
        [CliRoute("run")]
        [CliCommandExample("run --v[1] a")]
        public int Run([CliOption("--v")] Dictionary<int, string> value) => 0;
    }

    [System.ComponentModel.TypeConverter(typeof(TagListConverter))]
    public sealed class TagList
    {
        public TagList(string[] items) => Items = items;
        public string[] Items { get; }
    }

    public sealed class TagListConverter : System.ComponentModel.TypeConverter
    {
        public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object? ConvertFrom(
            System.ComponentModel.ITypeDescriptorContext? context,
            System.Globalization.CultureInfo? culture,
            object value) =>
            value is string text ? new TagList(text.Split(',')) : base.ConvertFrom(context, culture, value);
    }

    public sealed class TagListService
    {
        public TagList? Received;

        [CliRoute("run")]
        [CliCommandExample("run --v a,b")]
        public int Run([CliOption("--v")] TagList value)
        {
            Received = value;
            return 0;
        }
    }
}
