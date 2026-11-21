// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace ReferenceCorpus;

using Microsoft.CodeAnalysis;
using System.Xml.Linq;
using uk.osric.asp.Web.Documentation;

/// <summary>Copies an <c>inheritdoc</c> entry's text down from the symbol it inherits.</summary>
/// <remarks>
/// The XML says <c>&lt;inheritdoc/&gt;</c> and nothing else, and the compiler does not write the
/// source it meant. The metadata knows: an override's base definition, the interface member an
/// implementation maps to, or a type's base. The copy is best-effort; a source outside the corpus is
/// named and linked where the API browser has a page, and nothing here can fail the build.
/// </remarks>
internal static class SymbolInheritance {

    private const int MaxDepth = 8;

    private enum Outcome {
        None,
        Copied,
        Noted,
    }

    /// <summary>Resolves every inherit-only entry in place.</summary>
    /// <param name="documents">The corpus, already normalised and with its anchors.</param>
    /// <param name="symbols">Every symbol of every document, by documentation id.</param>
    /// <param name="entries">Every entry of every document, by documentation id.</param>
    /// <param name="log">Where the summary line goes.</param>
    public static void Resolve(
        IReadOnlyList<ReferenceDocument> documents,
        IReadOnlyDictionary<string, ISymbol> symbols,
        IReadOnlyDictionary<string, (ReferenceDocument Document, ReferenceEntry Entry)> entries,
        Action<string> log) {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(log);

        Dictionary<string, XElement> elements = [];
        foreach (ReferenceDocument document in documents) {
            foreach (XElement element in document.Content.Descendants().Where(item => item.Attribute("id") is not null)) {
                elements[element.Attribute("id")!.Value] = element;
            }
        }

        int copied = 0;
        int noted = 0;
        HashSet<string> active = [];
        foreach ((string id, XElement element) in elements) {
            if (!IsInheritOnly(element)) {
                continue;
            }

            switch (ResolveOne(id, element, elements, entries, symbols, active, depth: 0)) {
                case Outcome.Copied:
                    copied++;
                    break;
                case Outcome.Noted:
                    noted++;
                    break;
                default:
                    break;
            }
        }

        log($"Copied {copied} inherited documentation entries and noted {noted} whose source is elsewhere.");
    }

    private static Outcome ResolveOne(
        string id,
        XElement target,
        IReadOnlyDictionary<string, XElement> elements,
        IReadOnlyDictionary<string, (ReferenceDocument Document, ReferenceEntry Entry)> entries,
        IReadOnlyDictionary<string, ISymbol> symbols,
        HashSet<string> active,
        int depth) {
        if (depth > MaxDepth) {
            Note(target);
            return Outcome.Noted;
        }

        if (!active.Add(id)) {
            Note(target);
            return Outcome.Noted;
        }

        try {
            if (!symbols.TryGetValue(id, out ISymbol? symbol) || Source(symbol, entries) is not { } source) {
                Note(target);
                return Outcome.Noted;
            }

            string sourceId = Id(source);
            string display = Display(sourceId, entries);
            target.Elements("inheritdoc").Remove();
            target.SetAttributeValue("inherited-from", sourceId);
            target.SetAttributeValue("inherited-display", display);

            if (elements.TryGetValue(sourceId, out XElement? sourceElement)
                && !ReferenceEquals(sourceElement, target)
                && entries.TryGetValue(sourceId, out (ReferenceDocument Document, ReferenceEntry Entry) entry)) {
                if (IsInheritOnly(sourceElement)) {
                    _ = ResolveOne(sourceId, sourceElement, elements, entries, symbols, active, depth + 1);
                }

                if (!IsInheritOnly(sourceElement)) {
                    foreach (XElement child in sourceElement.Elements()
                        .Where(child => child.Name != "inheritdoc" && child.Name != "member")) {
                        target.Add(new XElement(child));
                    }

                    RewriteNames(target, symbol);
                    target.SetAttributeValue("inherited-href", ReferenceUrls.Page(entry.Document, entry.Entry));
                    return Outcome.Copied;
                }
            }

            if (ReferenceDisplay.LearnUrl(sourceId) is { } learn) {
                target.SetAttributeValue("inherited-href", learn);
            }

            return Outcome.Noted;
        } finally {
            _ = active.Remove(id);
        }
    }

    private static ISymbol? Source(
        ISymbol symbol,
        IReadOnlyDictionary<string, (ReferenceDocument Document, ReferenceEntry Entry)> entries) => symbol switch {
            INamedTypeSymbol type => Prefer(TypeSources(type), entries),
            IMethodSymbol method => SourceMethod(method, entries),
            IPropertySymbol property => SourceProperty(property, entries),
            IEventSymbol @event => SourceEvent(@event, entries),
            _ => null,
        };

    private static IEnumerable<ISymbol> TypeSources(INamedTypeSymbol type) {
        if (type.BaseType is { } baseType && baseType.SpecialType != SpecialType.System_Object) {
            yield return baseType;
        }

        foreach (INamedTypeSymbol contract in type.AllInterfaces) {
            yield return contract;
        }
    }

    private static ISymbol? SourceMethod(
        IMethodSymbol method,
        IReadOnlyDictionary<string, (ReferenceDocument Document, ReferenceEntry Entry)> entries) =>
        method.OverriddenMethod ?? Prefer(InterfaceSources(method), entries);

    private static ISymbol? SourceProperty(
        IPropertySymbol property,
        IReadOnlyDictionary<string, (ReferenceDocument Document, ReferenceEntry Entry)> entries) =>
        property.OverriddenProperty ?? Prefer(InterfaceSources(property), entries);

    private static ISymbol? SourceEvent(
        IEventSymbol @event,
        IReadOnlyDictionary<string, (ReferenceDocument Document, ReferenceEntry Entry)> entries) =>
        @event.OverriddenEvent ?? Prefer(InterfaceSources(@event), entries);

    private static IEnumerable<ISymbol> InterfaceSources(ISymbol member) {
        if (member.ContainingType is not { } type) {
            yield break;
        }

        foreach (INamedTypeSymbol contract in type.AllInterfaces) {
            foreach (ISymbol candidate in contract.GetMembers()) {
                if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(candidate), member)) {
                    yield return candidate;
                }
            }
        }
    }

    private static ISymbol? Prefer(
        IEnumerable<ISymbol> candidates,
        IReadOnlyDictionary<string, (ReferenceDocument Document, ReferenceEntry Entry)> entries) {
        List<ISymbol> all = [.. candidates];
        return all
            .OrderBy(candidate => entries.ContainsKey(Id(candidate)) ? 0 : 1)
            .ThenBy(Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    // The XML names the definition, not a constructed generic; a base type such as Bar<int> has to
    // give the id of Bar`1 or nothing in the corpus will match it.
    private static string Id(ISymbol symbol) =>
        symbol.OriginalDefinition.GetDocumentationCommentId() ?? symbol.ToDisplayString();

    private static string Display(
        string id,
        IReadOnlyDictionary<string, (ReferenceDocument Document, ReferenceEntry Entry)> entries) {
        if (entries.TryGetValue(id, out (ReferenceDocument Document, ReferenceEntry Entry) entry)) {
            if (entry.Entry.IsType || ReferenceDisplay.Owner(id) is not { } owner) {
                return entry.Entry.Text;
            }

            return entries.TryGetValue(owner, out (ReferenceDocument Document, ReferenceEntry Entry) source)
                ? source.Entry.Text + "." + entry.Entry.Text
                : entry.Entry.Text;
        }

        return ReferenceDisplay.CrefText(id);
    }

    private static void RewriteNames(XElement target, ISymbol symbol) {
        IReadOnlyList<string> parameters = symbol switch {
            IMethodSymbol method => [.. method.Parameters.Select(parameter => parameter.Name)],
            IPropertySymbol property => [.. property.Parameters.Select(parameter => parameter.Name)],
            _ => [],
        };

        IReadOnlyList<string> typeParameters = symbol switch {
            IMethodSymbol method => [.. method.TypeParameters.Select(parameter => parameter.Name)],
            INamedTypeSymbol type => [.. type.TypeParameters.Select(parameter => parameter.Name)],
            _ => [],
        };

        Rewrite(target.Elements("param"), parameters);
        Rewrite(target.Elements("typeparam"), typeParameters);
    }

    private static void Rewrite(IEnumerable<XElement> elements, IReadOnlyList<string> names) {
        int index = 0;
        foreach (XElement element in elements) {
            if (index < names.Count) {
                element.SetAttributeValue("name", names[index]);
            }

            index++;
        }
    }

    // A type element also holds its members, so only the type's own documentation is considered.
    private static bool IsInheritOnly(XElement element) {
        List<XElement> content = [.. element.Elements().Where(child => child.Name != "member")];
        return content.Any(child => child.Name == "inheritdoc")
            && content.All(child => child.Name == "inheritdoc")
            && !element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value));
    }

    private static void Note(XElement target) {
        target.Elements("inheritdoc").Remove();
        target.SetAttributeValue("inherits", "true");
    }
}
