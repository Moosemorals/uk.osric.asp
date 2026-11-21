// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Documentation;

using System.Net;
using System.Text;

/// <summary>Writes the reference's fragment from the corpus the build emitted.</summary>
/// <remarks>
/// The corpus is data: a declaration is already split into coloured runs, a doc block is a small
/// tree and every link is already resolved. This is the one place that decides what markup those
/// become, so a change to the page is a change here rather than a second declarative language.
/// </remarks>
internal static class ReferenceHtml {

    private static readonly (string Kind, string Label)[] s_kinds = [
        ("interface", "Interfaces"),
        ("class", "Classes"),
        ("record", "Records"),
        ("record struct", "Record structs"),
        ("struct", "Structs"),
        ("enum", "Enums"),
        ("delegate", "Delegates"),
    ];

    /// <summary>The type index and every type in one namespace.</summary>
    /// <param name="space">The namespace to write.</param>
    public static string Namespace(ReferenceNamespace space) {
        ArgumentNullException.ThrowIfNull(space);

        StringBuilder builder = new();
        _ = builder.Append(TypeIndex(space));
        foreach (ReferenceType type in space.Types) {
            _ = builder.Append(Type(type));
        }

        return builder.ToString();
    }

    /// <summary>One declaration, with its tokens coloured.</summary>
    /// <param name="declaration">The declaration to write.</param>
    public static string Declaration(ReferenceDeclaration declaration) {
        ArgumentNullException.ThrowIfNull(declaration);

        StringBuilder builder = new("<code class=\"declaration\">");
        foreach (ReferenceToken token in declaration.Tokens) {
            if (token.Href is { } href) {
                _ = builder.Append("<a");
                if (href.StartsWith('#', StringComparison.Ordinal)) {
                    // A parameter name is the far end of the row's back link: the row points here.
                    _ = builder.Append(" id=\"").Append(Encode(href[1..] + "-ref")).Append('"');
                }

                _ = builder.Append(" href=\"").Append(Encode(href)).Append("\">");
            }

            if (string.Equals(token.Kind, "text", StringComparison.Ordinal)) {
                _ = builder.Append(Encode(token.Text));
            } else {
                _ = builder.Append("<span class=\"tok-").Append(Encode(token.Kind)).Append("\">")
                    .Append(Encode(token.Text)).Append("</span>");
            }

            if (token.Href is not null) {
                _ = builder.Append("</a>");
            }
        }

        return builder.Append("</code>").ToString();
    }

    private static string TypeIndex(ReferenceNamespace space) {
        StringBuilder builder = new("<nav class=\"type-index\" aria-label=\"Types in this namespace\">");
        HashSet<string> shown = [];
        foreach ((string kind, string label) in s_kinds) {
            List<ReferenceType> types = [.. space.Types.Where(type => string.Equals(type.Kind, kind, StringComparison.Ordinal))];
            _ = shown.Add(kind);
            if (types.Count > 0) {
                Group(builder, label, types);
            }
        }

        List<ReferenceType> rest = [.. space.Types.Where(type => !shown.Contains(type.Kind))];
        if (rest.Count > 0) {
            Group(builder, "Other", rest);
        }

        return builder.Append("</nav>").ToString();
    }

    private static void Group(StringBuilder builder, string label, IReadOnlyList<ReferenceType> types) {
        _ = builder.Append("<div class=\"type-group\"><p class=\"type-group-label\">").Append(Encode(label))
            .Append("</p><ul class=\"type-list\">");
        foreach (ReferenceType type in types) {
            _ = builder.Append("<li><a href=\"#").Append(Encode(type.Anchor)).Append("\"><code>")
                .Append(Encode(type.Display)).Append("</code></a></li>");
        }

        _ = builder.Append("</ul></div>");
    }

    private static string Type(ReferenceType type) {
        StringBuilder builder = new($"<details class=\"type\" id=\"{Encode(type.Anchor)}\">");
        _ = builder.Append("<summary>").Append(Declaration(type.Declaration)).Append("</summary>")
            .Append("<div class=\"type-body\">")
            .Append(Summary(type.Summary))
            .Append(Inherited(type.Inherited))
            .Append(Remarks(type.Remarks))
            .Append(Parameters("Type parameters", type.TypeParameters, type.Anchor, "typeparam"));

        foreach ((string label, IReadOnlyList<ReferenceMember> members) in MemberGroups(type)) {
            _ = builder.Append("<h3 class=\"member-group\">").Append(Encode(label)).Append("</h3>");
            foreach (ReferenceMember member in members) {
                _ = builder.Append(Member(member));
            }
        }

        return builder.Append("</div></details>").ToString();
    }

    private static IReadOnlyList<(string Label, IReadOnlyList<ReferenceMember> Members)> MemberGroups(ReferenceType type) {
        List<(string Label, IReadOnlyList<ReferenceMember> Members)> groups = [];
        AddGroup("Constructors", IsConstructor);
        AddGroup(type.Kind == "enum" ? "Values" : "Fields",
            member => string.Equals(member.Kind, "Field", StringComparison.Ordinal));
        AddGroup("Properties", member => string.Equals(member.Kind, "Property", StringComparison.Ordinal));
        AddGroup("Methods", member => string.Equals(member.Kind, "Method", StringComparison.Ordinal) && !IsConstructor(member));
        AddGroup("Events", member => string.Equals(member.Kind, "Event", StringComparison.Ordinal));
        AddGroup("Other", member =>
            !IsConstructor(member)
            && member.Kind is not ("Field" or "Property" or "Method" or "Event"));

        return groups;

        void AddGroup(string label, Func<ReferenceMember, bool> match) {
            List<ReferenceMember> members = [.. type.Members.Where(match)];
            if (members.Count > 0) {
                groups.Add((label, members));
            }
        }
    }

    private static bool IsConstructor(ReferenceMember member) =>
        member.Id.Contains(".#ctor", StringComparison.Ordinal)
        || member.Id.Contains(".#cctor", StringComparison.Ordinal);

    private static string Member(ReferenceMember member) {
        StringBuilder builder = new("<div class=\"member\">");
        _ = builder.Append("<h4 id=\"").Append(Encode(member.Anchor)).Append("\">")
            .Append(Declaration(member.Declaration)).Append("</h4>")
            .Append(Summary(member.Summary))
            .Append(Inherited(member.Inherited))
            .Append(Remarks(member.Remarks))
            .Append(Parameters("Type parameters", member.TypeParameters, member.Anchor, "typeparam"))
            .Append(Parameters("Parameters", member.Parameters, member.Anchor, "param"))
            .Append(Block("Returns", member.Returns))
            .Append(Block("Value", member.Value))
            .Append(Exceptions(member.Exceptions));

        return builder.Append("</div>").ToString();
    }

    private static string Summary(ReferenceDoc doc) =>
        doc.Nodes.Count == 0 ? string.Empty : "<div class=\"doc-summary\">" + Nodes(doc.Nodes) + "</div>";

    private static string Remarks(ReferenceDoc doc) =>
        doc.Nodes.Count == 0 ? string.Empty : "<div class=\"doc-remarks\">" + Nodes(doc.Nodes) + "</div>";

    private static string Block(string label, ReferenceDoc doc) =>
        doc.Nodes.Count == 0
            ? string.Empty
            : "<p class=\"doc-label\">" + Encode(label) + "</p><div class=\"doc-block\">" + Nodes(doc.Nodes) + "</div>";

    private static string Inherited(ReferenceInherited? inherited) {
        if (inherited is null) {
            return string.Empty;
        }

        if (inherited.Unknown || inherited.From is null) {
            return "<p class=\"inherited\">Inherits its documentation.</p>";
        }

        string display = string.IsNullOrEmpty(inherited.Display) ? inherited.From : inherited.Display;
        return inherited.Href is null
            ? "<p class=\"inherited\">Inherited from <code>" + Encode(display) + "</code>.</p>"
            : "<p class=\"inherited\">Inherited from <a href=\"" + Encode(inherited.Href)
                + "\"><code>" + Encode(display) + "</code></a>.</p>";
    }

    private static string Parameters(
        string label,
        IReadOnlyList<ReferenceParameter> parameters,
        string owner,
        string kind) {
        if (parameters.Count == 0) {
            return string.Empty;
        }

        StringBuilder builder = new($"<p class=\"doc-label\">{Encode(label)}</p><dl class=\"parameters\">");
        foreach (ReferenceParameter parameter in parameters) {
            string target = owner + "-" + kind + "-" + parameter.Name;
            _ = builder.Append("<dt id=\"").Append(Encode(target))
                .Append("\"><code>").Append(Encode(parameter.Name)).Append("</code> ")
                .Append("<a class=\"back-link\" href=\"#").Append(Encode(target))
                .Append("-ref\" aria-label=\"Back to the signature\" title=\"Back to the signature\">↩</a>")
                .Append("</dt><dd>").Append(Nodes(parameter.Doc.Nodes)).Append("</dd>");
        }

        return builder.Append("</dl>").ToString();
    }

    private static string Exceptions(IReadOnlyList<ReferenceException> exceptions) {
        if (exceptions.Count == 0) {
            return string.Empty;
        }

        StringBuilder builder = new("<p class=\"doc-label\">Exceptions</p><dl class=\"exceptions\">");
        foreach (ReferenceException exception in exceptions) {
            _ = builder.Append("<dt>");
            if (exception.Href is null) {
                _ = builder.Append("<code>").Append(Encode(exception.Name)).Append("</code>");
            } else {
                _ = builder.Append("<a href=\"").Append(Encode(exception.Href)).Append("\"><code>")
                    .Append(Encode(exception.Name)).Append("</code></a>");
            }

            _ = builder.Append("</dt><dd>").Append(Nodes(exception.Doc.Nodes)).Append("</dd>");
        }

        return builder.Append("</dl>").ToString();
    }

    private static string Nodes(IReadOnlyList<ReferenceNode> nodes) {
        StringBuilder builder = new();
        foreach (ReferenceNode node in nodes) {
            _ = builder.Append(Node(node));
        }

        return builder.ToString();
    }

    private static string Node(ReferenceNode node) => node.Kind switch {
        "text" => Encode(node.Text ?? string.Empty),
        "para" => "<p>" + Children(node) + "</p>",
        "code" => "<code>" + Children(node) + "</code>",
        "pre" => "<pre class=\"source\" tabindex=\"0\"><code>" + Encode(node.Text ?? string.Empty) + "</code></pre>",
        "link" => "<a href=\"" + Encode(node.Href ?? string.Empty) + "\">" + Children(node) + "</a>",
        "strong" => "<strong>" + Children(node) + "</strong>",
        "em" => "<em>" + Children(node) + "</em>",
        "br" => "<br>",
        "term" => "<strong>" + Children(node) + "</strong>",
        "description" => Children(node),
        "item" => Item(node),
        "list" => List(node),
        "table" => Table(node),
        "row" => "<tr>" + Children(node) + "</tr>",
        "cell" => "<td>" + Children(node) + "</td>",
        "header" => "<th scope=\"col\">" + Children(node) + "</th>",
        _ => Children(node),
    };

    private static string Children(ReferenceNode node) =>
        node.Children is null ? string.Empty : Nodes(node.Children);

    private static string Item(ReferenceNode node) {
        ReferenceNode? term = node.Children?.FirstOrDefault(child => string.Equals(child.Kind, "term", StringComparison.Ordinal));
        ReferenceNode? description = node.Children?.FirstOrDefault(child => string.Equals(child.Kind, "description", StringComparison.Ordinal));
        if (term is null && description is null) {
            return "<li>" + Children(node) + "</li>";
        }

        StringBuilder builder = new("<li>");
        if (term is not null) {
            _ = builder.Append(Node(term));
            if (description is not null) {
                _ = builder.Append(" — ");
            }
        }

        if (description is not null) {
            _ = builder.Append(Node(description));
        }

        return builder.Append("</li>").ToString();
    }

    private static string List(ReferenceNode node) {
        string tag = node.Ordered ? "ol" : "ul";
        return "<" + tag + ">" + Children(node) + "</" + tag + ">";
    }

    private static string Table(ReferenceNode node) =>
        "<table class=\"doc-table\">" + Children(node) + "</table>";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
