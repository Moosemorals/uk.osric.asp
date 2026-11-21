// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Documentation;

using System.Xml.Linq;

/// <summary>One type or member as the corpus addresses it.</summary>
/// <param name="Id">The raw documentation id.</param>
/// <param name="Anchor">The URL fragment its section carries.</param>
/// <param name="Kind">The word for its kind.</param>
/// <param name="Display">Its signature, or a type's name with its type parameters.</param>
/// <param name="Text">Its name alone, for a sentence that links to it.</param>
/// <param name="Namespace">The namespace the entry is filed under.</param>
/// <param name="IsType">Whether it is a type section rather than a member.</param>
internal sealed record ReferenceEntry(
    string Id,
    string Anchor,
    string Kind,
    string Display,
    string Text,
    string Namespace,
    bool IsType);

/// <summary>The normalised documentation of one assembly.</summary>
/// <param name="Info">The assembly it describes.</param>
/// <param name="Content">The XML the stylesheet transforms.</param>
/// <param name="TypeCount">How many type sections it holds.</param>
/// <param name="EntryCount">How many documented entries the source file carries.</param>
/// <param name="Entries">Every type and member, by its raw id.</param>
internal sealed record ReferenceDocument(
    ReferenceAssemblyInfo Info,
    XDocument Content,
    int TypeCount,
    int EntryCount,
    IReadOnlyDictionary<string, ReferenceEntry> Entries) {

    /// <summary>Parses one XML file into the document the corpus and the stylesheet use.</summary>
    /// <param name="info">The assembly the file belongs to.</param>
    /// <param name="source">The parsed file.</param>
    /// <exception cref="InvalidDataException">The file has no <c>members</c> element.</exception>
    public static ReferenceDocument Build(ReferenceAssemblyInfo info, XDocument source) {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(source);

        string root = info.AssemblyName;
        XElement members = source.Root?.Element("members")
            ?? throw new InvalidDataException($"{info.AssemblyName}.xml has no members element.");

        List<XElement> sourceMembers = [.. members.Elements("member")];
        Dictionary<string, XElement> byId = [];
        foreach (XElement member in sourceMembers) {
            if (member.Attribute("name")?.Value is { Length: > 0 } id) {
                byId[id] = member;
            }
        }

        HashSet<string> typeIds = [.. byId.Keys.Where(id => id[0] == 'T')];
        HashSet<string> owners = [.. byId.Keys
            .Where(id => id[0] != 'T')
            .Select(ReferenceDisplay.Owner)
            .OfType<string>()];
        HashSet<string> allTypes = [.. typeIds, .. owners];

        HashSet<string> relativeTypes = [.. allTypes.Select(id => ReferenceDisplay.Relative(id, root))];
        Dictionary<string, TypeNames> parts = [];
        foreach (string relative in relativeTypes) {
            parts[relative] = TypeParts(relative, relativeTypes);
        }

        List<TypePlan> plans = [];
        foreach (string id in allTypes) {
            string relative = ReferenceDisplay.Relative(id, root);
            _ = byId.TryGetValue(id, out XElement? doc);
            TypePlan plan = new(
                id,
                relative,
                parts[relative],
                doc,
                [.. sourceMembers.Where(member => {
                    string memberId = member.Attribute("name")!.Value;
                    return memberId[0] != 'T'
                        && string.Equals(ReferenceDisplay.Owner(memberId), id, StringComparison.Ordinal);
                })]);
            plans.Add(plan);
        }

        List<TypePlan> ordered = [.. plans
            .OrderBy(plan => plan.Parts.Namespace.Length == 0 ? 0 : 1)
            .ThenBy(plan => plan.Parts.Namespace, StringComparer.Ordinal)
            .ThenBy(plan => plan.Parts.Name, StringComparer.Ordinal)
            .ThenBy(plan => plan.Id, StringComparer.Ordinal)];

        XElement output = new(
            "reference",
            new XAttribute("slug", info.Slug),
            new XAttribute("assembly", info.AssemblyName),
            new XAttribute("title", info.Title),
            new XAttribute("description", info.Description));

        HashSet<string> anchors = [];
        Dictionary<string, ReferenceEntry> entries = [];
        int typeCount = 0;

        foreach (IGrouping<string, TypePlan> group in ordered.GroupBy(plan => plan.Parts.Namespace, StringComparer.Ordinal)) {
            XElement namespaceElement = new("namespace", new XAttribute("name", group.Key));
            foreach (TypePlan plan in group) {
                typeCount++;
                List<string> typeParameters = Names(plan.Doc, "typeparam");
                string display = ReferenceDisplay.TypeDisplay(plan.Id, root, typeParameters);
                string anchor = Unique(
                    ReferenceDisplay.Anchor(plan.Id, root, typeParameters, []),
                    anchors);

                XElement typeElement = new(
                    "type",
                    new XAttribute("id", plan.Id),
                    new XAttribute("anchor", anchor),
                    new XAttribute("name", plan.Parts.Name),
                    new XAttribute("local", plan.Parts.Local),
                    new XAttribute("display", display));

                if (plan.Doc is not null) {
                    foreach (XElement child in plan.Doc.Elements()) {
                        typeElement.Add(new XElement(child));
                    }
                }

                entries[plan.Id] = new ReferenceEntry(
                    plan.Id, anchor, ReferenceDisplay.KindName('T'), display, display, plan.Parts.Namespace, IsType: true);

                foreach (XElement sourceMember in plan.Members) {
                    string id = sourceMember.Attribute("name")!.Value;
                    List<string> methodTypeParameters = Names(sourceMember, "typeparam");
                    List<string> parameterNames = Names(sourceMember, "param");
                    string memberDisplay = ReferenceDisplay.MemberDisplay(
                        id, root, typeParameters, methodTypeParameters, parameterNames);
                    string memberAnchor = Unique(
                        ReferenceDisplay.Anchor(id, root, typeParameters, methodTypeParameters),
                        anchors);

                    XElement memberElement = new(
                        "member",
                        new XAttribute("id", id),
                        new XAttribute("anchor", memberAnchor),
                        new XAttribute("kind", ReferenceDisplay.KindName(id[0])),
                        new XAttribute("display", memberDisplay));

                    foreach (XElement child in sourceMember.Elements()) {
                        memberElement.Add(new XElement(child));
                    }

                    typeElement.Add(memberElement);
                    entries[id] = new ReferenceEntry(
                        id,
                        memberAnchor,
                        ReferenceDisplay.KindName(id[0]),
                        memberDisplay,
                        ReferenceDisplay.MemberName(id, root, methodTypeParameters),
                        plan.Parts.Namespace,
                        IsType: false);
                }

                namespaceElement.Add(typeElement);
            }

            output.Add(namespaceElement);
            namespaceElement.SetAttributeValue("type-count", namespaceElement.Elements("type").Count());
        }

        return new ReferenceDocument(info, new XDocument(output), typeCount, sourceMembers.Count, entries);
    }

    private static List<string> Names(XElement? element, string child) {
        if (element is null) {
            return [];
        }

        return [.. element.Elements(child)
            .Select(item => item.Attribute("name")?.Value)
            .OfType<string>()
            .Where(name => name.Length > 0)];
    }

    private static string Unique(string anchor, HashSet<string> used) {
        if (used.Add(anchor)) {
            return anchor;
        }

        for (int suffix = 2; ; suffix++) {
            string candidate = anchor + "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (used.Add(candidate)) {
                return candidate;
            }
        }
    }

    // A nested type is a type whose relative name starts with another documented type's name and a
    // dot; the longest such prefix is the containing type. Every other type splits at its last dot.
    private static TypeNames TypeParts(string relative, IReadOnlySet<string> known) {
        string? outer = null;
        foreach (string candidate in known) {
            if (relative.Length > candidate.Length + 1
                && relative.StartsWith(candidate + ".", StringComparison.Ordinal)
                && (outer is null || candidate.Length > outer.Length)) {
                outer = candidate;
            }
        }

        if (outer is not null) {
            TypeNames parent = TypeParts(outer, known);
            int dot = relative.LastIndexOf('.');
            return new TypeNames(
                parent.Namespace,
                parent.Name + relative[outer.Length..],
                relative[(dot + 1)..]);
        }

        int index = relative.LastIndexOf('.');
        string local = index < 0 ? relative : relative[(index + 1)..];
        return index < 0
            ? new TypeNames(string.Empty, local, local)
            : new TypeNames(relative[..index], local, local);
    }

    private sealed record TypePlan(
        string Id,
        string Relative,
        TypeNames Parts,
        XElement? Doc,
        IReadOnlyList<XElement> Members);

    private sealed record TypeNames(string Namespace, string Name, string Local);
}
