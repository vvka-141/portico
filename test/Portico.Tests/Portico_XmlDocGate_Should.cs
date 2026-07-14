using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
// It reflects over the built assembly and cross-references the shipped Portico.xml, because that is
// the surface a consumer actually sees. A source-grep is misleading here: CliApplication's private
// nested Builder has `public` members that are not exported, and a grep reports ~130 phantom gaps.
public sealed class Portico_XmlDocGate_Should
{
    private static readonly Assembly Portico = typeof(CliApplication).Assembly;

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
        var documented = DocumentedMembers("T:");

        var undocumented = Portico.GetExportedTypes()
            .Where(t => !IsCompilerGenerated(t))
            .Select(t => t.FullName!)
            .Where(name => !documented.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            undocumented.Count == 0,
            "CHARTER §6.5: every exported type needs an XML <summary>. Missing on:" +
            Environment.NewLine + string.Join(Environment.NewLine, undocumented));
    }

    [Fact]
    public void DocumentEveryPublicMethod()
    {
        var documented = DocumentedMembers("M:");

        var undocumented = PublicMethods()
            .Select(MemberKey)
            .Distinct(StringComparer.Ordinal)
            .Where(key => !documented.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            undocumented.Count == 0,
            "CHARTER §6.5: every public method needs an XML <summary>. Missing on:" +
            Environment.NewLine + string.Join(Environment.NewLine, undocumented));
    }

    [Fact]
    public void GiveEveryPublicMethodAUsageForm()
    {
        var exampled = DocumentedMembers("M:", requireExample: true);

        var missing = PublicMethods()
            .Select(MemberKey)
            .Distinct(StringComparer.Ordinal)
            .Where(key => !ExampleExempt.Contains(key))
            .Where(key => !exampled.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "CHARTER §6.5: every public method needs at least one <example> — an agent pattern-matches " +
            "from usage forms, not prose. Missing on:" +
            Environment.NewLine + string.Join(Environment.NewLine, missing) +
            Environment.NewLine +
            "If a member is genuinely exampleless per CHARTER §6.5, add it to ExampleExempt and say why.");
    }

    // Public, user-callable methods on the exported surface. Constructors are excluded: CHARTER §6.5
    // exempts them wholesale (the attribute types' canonical form is shown on the type, and an
    // exception ctor needs no worked example).
    private static IEnumerable<MethodInfo> PublicMethods() =>
        Portico.GetExportedTypes()
            .Where(t => !IsCompilerGenerated(t))
            .SelectMany(t => t.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName)          // property/event accessors, operators
            .Where(m => !IsCompilerGenerated(m))   // record ToString/Equals/<Clone>$/Deconstruct
            .Where(m => m.GetBaseDefinition().DeclaringType != typeof(object));

    /// <summary>
    /// Overloads collapse to one key. A single usage form on <c>GetLine</c> is enough for an agent to
    /// pattern-match the family; demanding one per overload would produce documentation nobody reads
    /// and a gate people learn to route around.
    /// </summary>
    private static string MemberKey(MethodInfo method) =>
        $"{method.DeclaringType!.FullName}.{method.Name}";

    private static readonly XDocument Docs = LoadDocs();

    private static XDocument LoadDocs()
    {
        var path = Path.ChangeExtension(Portico.Location, ".xml");
        Assert.True(
            File.Exists(path),
            $"Portico.xml is not next to Portico.dll ({path}). The XML-doc gate cannot be checked without " +
            "it — GenerateDocumentationFile must stay on.");
        return XDocument.Load(path);
    }

    /// <summary>
    /// The documented member keys of the given kind (<c>T:</c> types, <c>M:</c> methods), normalized
    /// to match reflection: parameter lists dropped (overloads collapse), arity suffixes stripped.
    /// </summary>
    private static HashSet<string> DocumentedMembers(string prefix, bool requireExample = false)
    {
        var members = Docs.Root?.Element("members")?.Elements("member") ?? [];

        var keys = members
            .Where(m => HasProse(m, requireExample))
            .Select(m => (string?)m.Attribute("name"))
            .Where(name => name is not null && name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => Normalize(name![prefix.Length..]));

        return new HashSet<string>(keys, StringComparer.Ordinal);
    }

    private static bool HasProse(XElement member, bool requireExample)
    {
        var element = requireExample ? member.Element("example") : member.Element("summary");
        return element is not null && !string.IsNullOrWhiteSpace(element.Value);
    }

    private static string Normalize(string docId)
    {
        var parenthesis = docId.IndexOf('(', StringComparison.Ordinal);
        if (parenthesis >= 0) docId = docId[..parenthesis];

        // Generic method arity (``1) is not part of the reflected method name; generic type arity
        // (`1) is part of Type.FullName, so only the method form is stripped.
        var arity = docId.IndexOf("``", StringComparison.Ordinal);
        return arity >= 0 ? docId[..arity] : docId;
    }

    private static bool IsCompilerGenerated(MemberInfo member) =>
        member.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;
}
