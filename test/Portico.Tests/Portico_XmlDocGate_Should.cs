using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// CHARTER §6.5, the agent-first release gate: every exported type and public method carries an XML
// <summary>, and every public method carries at least one <example>. Portico's public surface is
// where agents read the API and pattern-match usage; an entry point with no prose and no usage form
// is an agent-training problem shipped downstream.
//
// This test exists because the gate DECAYED. It was inherited from the origin marked "✅ audited"
// and was simply false when checked: CliApplication.Create — the primary entry point — had no
// <summary> at all. A gate nobody re-runs is decoration, so the gate now runs on every build.
//
// It reflects over the built assemblies and cross-references the shipped XML docs, because that is
// the surface a consumer actually sees. A source-grep is misleading here: CliApplication's private
// nested Builder has `public` members that are not exported, and a grep reports ~130 phantom gaps.
//
// POR-105: the gate covers ALL shipped assemblies (core + adapters), checks <summary> per
// SIGNATURE (not per family), and checks <example> per FAMILY (one usage form is enough for an
// agent to pattern-match the overload set).
public sealed class Portico_XmlDocGate_Should
{
    private static readonly Assembly[] GatedAssemblies =
    [
        typeof(CliApplication).Assembly,
        typeof(Portico.DependencyInjection.CliApplicationBuilderExtensions).Assembly,
        typeof(Portico.Hosting.CliHostExtensions).Assembly,
    ];

    private static readonly Dictionary<string, XDocument> DocsByPath = LoadAllDocs();

    /// <summary>
    /// Members that intentionally ship without an <c>&lt;example&gt;</c>, per CHARTER §6.5's own
    /// exemption list. This is the specification, not a convenience hatch: adding an entry here is a
    /// charter-level decision, and "I did not want to write an example" is not one.
    /// </summary>
    private static readonly HashSet<string> ExampleExempt = new(StringComparer.Ordinal)
    {
        // Value carriers: the shape IS the documentation. An example of a getter reads as noise.
        "Portico.CliInvocation.ToString",
        "Portico.CliFlag.ToString",
        "Portico.Testing.CliTestRunResult.ToString",

        // Equality/deconstruction on the public records — the C# language defines these, not us.
        "Portico.CliFlag.Equals",
        "Portico.CliFlag.GetHashCode",
        "Portico.Testing.CliTestRunResult.Equals",
        "Portico.Testing.CliTestRunResult.GetHashCode",
        "Portico.Testing.CliContractExample.Equals",
        "Portico.Testing.CliContractExample.GetHashCode",
        "Portico.Testing.CliContractExample.ToString",
    };

    [Fact]
    public void DocumentEveryExportedType()
    {
        var undocumented = new List<string>();

        foreach (var assembly in GatedAssemblies)
        {
            var docs = DocsFor(assembly);
            var documented = DocumentedTypeNames(docs);

            undocumented.AddRange(
                assembly.GetExportedTypes()
                    .Where(t => !IsCompilerGenerated(t))
                    .Select(t => t.FullName!)
                    .Where(name => !documented.Contains(name)));
        }

        undocumented.Sort(StringComparer.Ordinal);

        Assert.True(
            undocumented.Count == 0,
            "CHARTER §6.5: every exported type needs an XML <summary>. Missing on:" +
            Environment.NewLine + string.Join(Environment.NewLine, undocumented));
    }

    [Fact]
    public void DocumentEveryPublicMethodSignature()
    {
        var undocumented = new List<string>();

        foreach (var assembly in GatedAssemblies)
        {
            var docs = DocsFor(assembly);
            var documentedIds = DocumentedMethodIds(docs);

            foreach (var method in PublicMethods(assembly))
            {
                var id = BuildDocId(method);
                if (!documentedIds.Contains(id))
                    undocumented.Add(id);
            }
        }

        undocumented.Sort(StringComparer.Ordinal);

        Assert.True(
            undocumented.Count == 0,
            "CHARTER §6.5: every public method signature needs an XML <summary>. " +
            "One <example> per family is enough, but each overload needs its own <summary>. Missing on:" +
            Environment.NewLine + string.Join(Environment.NewLine, undocumented));
    }

    /// <remarks>
    /// <c>&lt;example&gt;</c> is checked per overload FAMILY, not per signature. One usage form on
    /// <c>Run</c> is enough for an agent to pattern-match the family; demanding one per overload
    /// would produce documentation nobody reads and a gate people learn to route around. This is
    /// deliberate — CHARTER §6.5. <c>&lt;summary&gt;</c> is per-signature because each overload's
    /// parameters deserve their own description.
    /// </remarks>
    [Fact]
    public void GiveEveryPublicMethodFamilyAUsageForm()
    {
        var missing = new List<string>();

        foreach (var assembly in GatedAssemblies)
        {
            var docs = DocsFor(assembly);
            var exampled = ExampledFamilyKeys(docs);

            missing.AddRange(
                PublicMethods(assembly)
                    .Select(FamilyKey)
                    .Distinct(StringComparer.Ordinal)
                    .Where(key => !ExampleExempt.Contains(key))
                    .Where(key => !exampled.Contains(key)));
        }

        missing.Sort(StringComparer.Ordinal);

        Assert.True(
            missing.Count == 0,
            "CHARTER §6.5: every public method family needs at least one <example> — an agent " +
            "pattern-matches from usage forms, not prose. Missing on:" +
            Environment.NewLine + string.Join(Environment.NewLine, missing) +
            Environment.NewLine +
            "If a member is genuinely exampleless per CHARTER §6.5, add it to ExampleExempt and say why.");
    }

    private static IEnumerable<MethodInfo> PublicMethods(Assembly assembly) =>
        assembly.GetExportedTypes()
            .Where(t => !IsCompilerGenerated(t))
            .SelectMany(t => t.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName)
            .Where(m => !IsCompilerGenerated(m))
            .Where(m => m.GetBaseDefinition().DeclaringType != typeof(object));

    private static string FamilyKey(MethodInfo method) =>
        $"{method.DeclaringType!.FullName}.{method.Name}";

    private static XDocument DocsFor(Assembly assembly)
    {
        var path = Path.ChangeExtension(assembly.Location, ".xml");
        if (DocsByPath.TryGetValue(path, out var cached))
            return cached;

        Assert.Fail(
            $"XML doc file not found for {assembly.GetName().Name} at {path}. " +
            "GenerateDocumentationFile must be on.");
        return null!;
    }

    private static Dictionary<string, XDocument> LoadAllDocs()
    {
        var result = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in new[]
        {
            typeof(CliApplication).Assembly,
            typeof(DependencyInjection.CliApplicationBuilderExtensions).Assembly,
            typeof(Hosting.CliHostExtensions).Assembly,
        })
        {
            var path = Path.ChangeExtension(assembly.Location, ".xml");
            Assert.True(
                File.Exists(path),
                $"{assembly.GetName().Name}.xml is not next to the DLL ({path}). " +
                "GenerateDocumentationFile must stay on.");
            result[path] = XDocument.Load(path);
        }
        return result;
    }

    private static HashSet<string> DocumentedTypeNames(XDocument docs)
    {
        var members = docs.Root?.Element("members")?.Elements("member") ?? [];
        return new HashSet<string>(
            members
                .Where(m => HasSummary(m))
                .Select(m => (string?)m.Attribute("name"))
                .Where(name => name is not null && name.StartsWith("T:", StringComparison.Ordinal))
                .Select(name => name!["T:".Length..]),
            StringComparer.Ordinal);
    }

    private static HashSet<string> DocumentedMethodIds(XDocument docs)
    {
        var members = docs.Root?.Element("members")?.Elements("member") ?? [];
        return new HashSet<string>(
            members
                .Where(m => HasSummary(m))
                .Select(m => (string?)m.Attribute("name"))
                .Where(name => name is not null && name.StartsWith("M:", StringComparison.Ordinal))
                .Select(name => NormalizeDocId(name!["M:".Length..])),
            StringComparer.Ordinal);
    }

    private static HashSet<string> ExampledFamilyKeys(XDocument docs)
    {
        var members = docs.Root?.Element("members")?.Elements("member") ?? [];
        return new HashSet<string>(
            members
                .Where(m => HasExample(m))
                .Select(m => (string?)m.Attribute("name"))
                .Where(name => name is not null && name.StartsWith("M:", StringComparison.Ordinal))
                .Select(name => NormalizeFamilyKey(name!["M:".Length..])),
            StringComparer.Ordinal);
    }

    private static bool HasSummary(XElement member) =>
        member.Element("summary") is { } s && !string.IsNullOrWhiteSpace(s.Value);

    private static bool HasExample(XElement member) =>
        member.Element("example") is { } e && !string.IsNullOrWhiteSpace(e.Value);

    private static string NormalizeDocId(string docId)
    {
        // Strip generic method arity (``1) but keep type arity (`1) and parameters.
        var arity = docId.IndexOf("``", StringComparison.Ordinal);
        if (arity >= 0)
        {
            var afterArity = arity + 2;
            while (afterArity < docId.Length && char.IsDigit(docId[afterArity]))
                afterArity++;
            docId = docId[..arity] + docId[afterArity..];
        }
        return docId;
    }

    private static string NormalizeFamilyKey(string docId)
    {
        // Strip parameters and generic method arity — family key is just Type.Name.
        var paren = docId.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0) docId = docId[..paren];
        var arity = docId.IndexOf("``", StringComparison.Ordinal);
        return arity >= 0 ? docId[..arity] : docId;
    }

    /// <summary>
    /// Builds the XML doc comment ID for a reflected method, matching the format the compiler
    /// emits. Generic method arity (<c>``N</c>) is stripped from both sides so a method like
    /// <c>ForApplication&lt;T&gt;()</c> matches regardless of where the arity sits in the ID.
    /// </summary>
    private static string BuildDocId(MethodInfo method)
    {
        var sb = new StringBuilder();
        sb.Append(method.DeclaringType!.FullName!.Replace('+', '.'));
        sb.Append('.');
        sb.Append(method.Name);

        var parameters = method.GetParameters();
        if (parameters.Length > 0)
        {
            sb.Append('(');
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0) sb.Append(',');
                AppendDocType(sb, parameters[i].ParameterType);
            }
            sb.Append(')');
        }

        return sb.ToString();
    }

    private static void AppendDocType(StringBuilder sb, Type type)
    {
        if (type.IsGenericParameter)
        {
            sb.Append(type.DeclaringMethod is not null ? "``" : "`");
            sb.Append(type.GenericParameterPosition);
            return;
        }

        if (type.IsByRef)
        {
            AppendDocType(sb, type.GetElementType()!);
            sb.Append('@');
            return;
        }

        if (type.IsArray)
        {
            AppendDocType(sb, type.GetElementType()!);
            sb.Append("[]");
            return;
        }

        if (type.IsGenericType)
        {
            var fullName = type.GetGenericTypeDefinition().FullName!.Replace('+', '.');
            var backtick = fullName.IndexOf('`');
            sb.Append(fullName[..backtick]);
            sb.Append('{');
            var args = type.GetGenericArguments();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(',');
                AppendDocType(sb, args[i]);
            }
            sb.Append('}');
            return;
        }

        sb.Append((type.FullName ?? type.Name).Replace('+', '.'));
    }

    private static bool IsCompilerGenerated(MemberInfo member) =>
        member.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;
}
