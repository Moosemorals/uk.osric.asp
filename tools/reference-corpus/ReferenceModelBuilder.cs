// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace ReferenceCorpus;

using System.Xml.Linq;
using uk.osric.asp.Web.Documentation;

/// <summary>Turns the annotated normalised tree into the corpus a page and the assistant read.</summary>
internal static class ReferenceModelBuilder {

    /// <summary>Builds one assembly's corpus.</summary>
    /// <param name="document">The normalised tree, after inheritdoc and cref links.</param>
    /// <param name="declarations">Declarations by documentation id.</param>
    public static ReferenceCorpus Build(
        ReferenceDocument document,
        IReadOnlyDictionary<string, ReferenceDeclaration> declarations) {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(declarations);

        XElement root = document.Content.Root
            ?? throw new InvalidDataException($"{document.Info.AssemblyName} has no reference root.");

        List<ReferenceNamespace> namespaces = [];
        foreach (XElement space in root.Elements("namespace")) {
            string relative = (string?)space.Attribute("name") ?? string.Empty;
            string name = relative.Length == 0
                ? document.Info.AssemblyName
                : document.Info.AssemblyName + "." + relative;
            List<ReferenceType> types = [.. space.Elements("type").Select(type => Type(type, relative, declarations))];
            namespaces.Add(new ReferenceNamespace(name, types.Count, types));
        }

        return new ReferenceCorpus(
            document.Info.Slug,
            document.Info.AssemblyName,
            document.Info.Title,
            document.Info.Description,
            document.TypeCount,
            document.EntryCount,
            namespaces);
    }

    private static ReferenceType Type(
        XElement type,
        string @namespace,
        IReadOnlyDictionary<string, ReferenceDeclaration> declarations) {
        string id = (string?)type.Attribute("id") ?? string.Empty;
        string anchor = (string?)type.Attribute("anchor") ?? string.Empty;
        string display = (string?)type.Attribute("display") ?? string.Empty;
        if (@namespace.Length > 0 && display.StartsWith(@namespace + ".", StringComparison.Ordinal)) {
            display = display[(@namespace.Length + 1)..];
        }

        return new ReferenceType(
            id,
            anchor,
            (string?)type.Attribute("name") ?? string.Empty,
            display,
            (string?)type.Attribute("type-kind") ?? "type",
            Link(Declaration(id, (string?)type.Attribute("declaration"), declarations), anchor),
            Doc(type.Element("summary")),
            Doc(type.Element("remarks")),
            Parameters(type, "typeparam"),
            Inherited(type),
            [.. type.Elements("member").Select(member => Member(member, declarations))]);
    }

    private static ReferenceMember Member(
        XElement member,
        IReadOnlyDictionary<string, ReferenceDeclaration> declarations) {
        string id = (string?)member.Attribute("id") ?? string.Empty;
        string anchor = (string?)member.Attribute("anchor") ?? string.Empty;
        return new ReferenceMember(
            id,
            anchor,
            (string?)member.Attribute("kind") ?? "Member",
            (string?)member.Attribute("display") ?? string.Empty,
            Link(Declaration(id, (string?)member.Attribute("declaration"), declarations), anchor),
            Doc(member.Element("summary")),
            Doc(member.Element("remarks")),
            Parameters(member, "typeparam"),
            Parameters(member, "param"),
            Doc(member.Element("returns")),
            Doc(member.Element("value")),
            Exceptions(member),
            Inherited(member));
    }

    // A parameter name has no page of its own, so its link is the entry in this member's parameter
    // list; the list writes the matching id. A type name's page was resolved by the corpus already.
    private static ReferenceDeclaration Link(ReferenceDeclaration declaration, string anchor) {
        if (!declaration.Tokens.Any(token => token.Kind is "param" or "typeparam")) {
            return declaration;
        }

        string self = "#" + anchor;
        List<ReferenceToken> tokens = [.. declaration.Tokens.Select(token => {
            ReferenceToken linked = token.Kind switch {
                "param" => token with { Href = "#" + anchor + "-param-" + token.Text.TrimStart('@') },
                "typeparam" => token with { Href = "#" + anchor + "-typeparam-" + token.Text.TrimStart('@') },
                _ => token,
            };

            // The type's own name points at the section the reader is already in; a link there is noise.
            return linked.Href is { } href && href.EndsWith(self, StringComparison.Ordinal)
                ? linked with { Href = null }
                : linked;
        })];

        return new ReferenceDeclaration(declaration.Text, tokens);
    }

    private static ReferenceDeclaration Declaration(
        string id,
        string? text,
        IReadOnlyDictionary<string, ReferenceDeclaration> declarations) {
        if (declarations.TryGetValue(id, out ReferenceDeclaration? declaration)) {
            return declaration;
        }

        string fallback = text ?? id;
        return new ReferenceDeclaration(fallback, [new ReferenceToken("text", fallback)]);
    }

    private static IReadOnlyList<ReferenceParameter> Parameters(XElement element, string name) =>
        [.. element.Elements(name).Select(parameter => new ReferenceParameter(
            (string?)parameter.Attribute("name") ?? string.Empty,
            Doc(parameter)))];

    private static IReadOnlyList<ReferenceException> Exceptions(XElement member) =>
        [.. member.Elements("exception").Select(exception => new ReferenceException(
            (string?)exception.Attribute("text") ?? (string?)exception.Attribute("cref") ?? string.Empty,
            (string?)exception.Attribute("href"),
            Doc(exception)))];

    private static ReferenceInherited? Inherited(XElement element) {
        if ((string?)element.Attribute("inherits") == "true") {
            return new ReferenceInherited(null, null, null, Unknown: true);
        }

        if ((string?)element.Attribute("inherited-from") is { } from) {
            return new ReferenceInherited(
                from,
                (string?)element.Attribute("inherited-display"),
                (string?)element.Attribute("inherited-href"),
                Unknown: false);
        }

        return null;
    }

    private static ReferenceDoc Doc(XElement? block) =>
        block is null
            ? ReferenceDoc.Empty
            : new ReferenceDoc(ReferenceText.Flatten(block), [.. Nodes(block)]);

    private static IEnumerable<ReferenceNode> Nodes(XElement element) {
        foreach (XNode node in element.Nodes()) {
            if (Node(node) is { } built) {
                yield return built;
            }
        }
    }

    private static ReferenceNode? Node(XNode node) => node switch {
        XCData data => Text(data.Value),
        XText text => Text(text.Value),
        XElement element => Node(element),
        _ => null,
    };

    // HTML collapses runs of whitespace, so the indentation the compiler writes into a doc block is
    // noise; one space preserves the separation and keeps the corpus a third smaller. The space at
    // either edge is kept because it separates inline runs: dropping it joins `await` to `is`.
    private static ReferenceNode? Text(string value) {
        if (value.Length == 0) {
            return null;
        }

        bool leading = char.IsWhiteSpace(value[0]);
        bool trailing = char.IsWhiteSpace(value[^1]);
        string tidy = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (tidy.Length == 0) {
            return new ReferenceNode("text", Text: " ");
        }

        return new ReferenceNode(
            "text",
            Text: (leading ? " " : string.Empty) + tidy + (trailing ? " " : string.Empty));
    }

    private static ReferenceNode? Node(XElement element) => element.Name.LocalName switch {
        "inheritdoc" => null,
        "para" => new ReferenceNode("para", Children: [.. Nodes(element)]),
        "c" => new ReferenceNode("code", Children: [.. Nodes(element)]),
        "code" => new ReferenceNode("pre", Text: element.Value),
        "see" => See(element),
        "paramref" or "typeparamref" =>
            new ReferenceNode("code", Text: (string?)element.Attribute("name") ?? string.Empty),
        "b" or "strong" => new ReferenceNode("strong", Children: [.. Nodes(element)]),
        "i" or "em" => new ReferenceNode("em", Children: [.. Nodes(element)]),
        "br" => new ReferenceNode("br"),
        "list" => List(element),
        "item" => Item(element),
        "term" => new ReferenceNode("term", Children: [.. Nodes(element)]),
        "description" => new ReferenceNode("description", Children: [.. Nodes(element)]),
        "listheader" => Row(element, "header"),
        _ => new ReferenceNode("unknown", Children: [.. Nodes(element)]),
    };

    private static ReferenceNode See(XElement element) {
        if ((string?)element.Attribute("href") is { Length: > 0 } href) {
            return new ReferenceNode(
                "link",
                Href: href,
                Children: element.Nodes().Any()
                    ? [.. Nodes(element)]
                    : [new ReferenceNode("code", Children: [new ReferenceNode("text", Text: SeeText(element))])]);
        }

        return new ReferenceNode(
            "code",
            Children: element.Nodes().Any()
                ? [.. Nodes(element)]
                : [new ReferenceNode("text", Text: SeeText(element))]);
    }

    private static string SeeText(XElement element) =>
        (string?)element.Attribute("text")
        ?? (string?)element.Attribute("langword")
        ?? (string?)element.Attribute("href")
        ?? string.Empty;

    private static ReferenceNode List(XElement element) {
        if ((string?)element.Attribute("type") == "table") {
            return new ReferenceNode("table", Children: [
                .. element.Elements().Select(row => Row(
                    row,
                    string.Equals(row.Name.LocalName, "listheader", StringComparison.Ordinal) ? "header" : "cell")),
            ]);
        }

        return new ReferenceNode(
            "list",
            Ordered: (string?)element.Attribute("type") == "number",
            Children: [.. element.Elements("item").Select(Item)]);
    }

    private static ReferenceNode Item(XElement element) {
        bool hasParts = element.Element("term") is not null || element.Element("description") is not null;
        return hasParts
            ? new ReferenceNode("item", Children: [.. element.Elements().Select(Node).OfType<ReferenceNode>()])
            : new ReferenceNode("item", Children: [.. Nodes(element)]);
    }

    private static ReferenceNode Row(XElement row, string cellKind) =>
        new(
            "row",
            Children: [.. row.Elements().Select(cell => new ReferenceNode(cellKind, Children: [.. Nodes(cell)]))]);
}
