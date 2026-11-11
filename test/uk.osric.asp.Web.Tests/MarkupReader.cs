// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Net;

/// <summary>Reads the elements, attributes and nesting out of a rendered document.</summary>
/// <remarks>
/// A hand-written reader is enough because this repository writes the markup itself and encodes every
/// value at the boundary, so a quote or an angle bracket never appears inside an attribute value. It
/// is therefore not a browser and not a validator: it knows what the views emit and nothing more. A
/// third-party parser would be a dependency taken to check our own output.
/// </remarks>
internal sealed class MarkupReader {

    private static readonly HashSet<string> VoidElements = [
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
    ];

    private MarkupReader(MarkupElement root) => Root = root;

    /// <summary>The document, whose children are its top-level elements.</summary>
    public MarkupElement Root { get; }

    /// <summary>Reads a document into elements.</summary>
    /// <param name="html">The rendered document.</param>
    public static MarkupReader Parse(string html) {
        ArgumentNullException.ThrowIfNull(html);

        MarkupElement root = new("#document");
        Stack<MarkupElement> open = new();
        open.Push(root);

        int position = 0;
        while (position < html.Length) {
            int start = html.IndexOf('<', position);
            if (start < 0) {
                AppendText(open, html[position..]);
                break;
            }

            if (start > position) {
                AppendText(open, html[position..start]);
            }

            if (string.CompareOrdinal(html, start, "<!--", 0, 4) == 0) {
                position = SkipPast(html, start, "-->");
                continue;
            }

            // The doctype and any other declaration: nothing to read.
            if (string.CompareOrdinal(html, start, "<!", 0, 2) == 0) {
                position = SkipPast(html, start, ">");
                continue;
            }

            if (string.CompareOrdinal(html, start, "</", 0, 2) == 0) {
                int close = SkipPast(html, start, ">");
                string name = html[(start + 2)..Math.Max(start + 2, close - 1)].Trim().ToLowerInvariant();
                Close(open, name);
                position = close;
                continue;
            }

            (MarkupElement element, bool isSelfClosing, int next) = ReadStartTag(html, start, open.Peek());
            open.Peek().Add(element);
            position = next;

            if (!isSelfClosing && !VoidElements.Contains(element.Name)) {
                open.Push(element);
            }
        }

        return new MarkupReader(root);
    }

    /// <summary>Every element with this name, in document order.</summary>
    /// <param name="name">The element name, lower-case.</param>
    public IReadOnlyList<MarkupElement> Elements(string name) {
        ArgumentNullException.ThrowIfNull(name);

        return [.. Root.Descendants().Where(element => string.Equals(element.Name, name, StringComparison.Ordinal))];
    }

    private static (MarkupElement Element, bool IsSelfClosing, int Next) ReadStartTag(
        string html,
        int start,
        MarkupElement parent) {

        int position = start + 1;
        int nameEnd = position;
        while (nameEnd < html.Length && !char.IsWhiteSpace(html[nameEnd]) && html[nameEnd] is not ('>' or '/')) {
            nameEnd++;
        }

        MarkupElement element = new(html[position..nameEnd].ToLowerInvariant(), parent);
        position = nameEnd;

        while (position < html.Length) {
            while (position < html.Length && char.IsWhiteSpace(html[position])) {
                position++;
            }

            if (position >= html.Length || html[position] == '>') {
                position = Math.Min(position + 1, html.Length);
                break;
            }

            if (html[position] == '/') {
                position++;
                continue;
            }

            int attributeEnd = position;
            while (attributeEnd < html.Length
                && !char.IsWhiteSpace(html[attributeEnd])
                && html[attributeEnd] is not ('=' or '>' or '/')) {
                attributeEnd++;
            }

            string name = html[position..attributeEnd].ToLowerInvariant();
            position = attributeEnd;
            string value = string.Empty;

            if (position < html.Length && html[position] == '=') {
                position++;
                if (position < html.Length && html[position] is '"' or '\'') {
                    char quote = html[position];
                    int valueEnd = html.IndexOf(quote, position + 1);
                    if (valueEnd < 0) {
                        valueEnd = html.Length;
                    }

                    value = html[(position + 1)..valueEnd];
                    position = Math.Min(valueEnd + 1, html.Length);
                } else {
                    int valueEnd = position;
                    while (valueEnd < html.Length && !char.IsWhiteSpace(html[valueEnd]) && html[valueEnd] != '>') {
                        valueEnd++;
                    }

                    value = html[position..valueEnd];
                    position = valueEnd;
                }
            }

            if (name.Length > 0) {
                element.Set(name, WebUtility.HtmlDecode(value));
            }
        }

        bool isSelfClosing = position >= 2 && html[position - 1] == '>' && html[position - 2] == '/';

        return (element, isSelfClosing, position);
    }

    private static void Close(Stack<MarkupElement> open, string name) {
        // An end tag with no start is ignored, and one that closes an ancestor closes everything
        // opened inside it, which is how a browser recovers from the same markup.
        if (!open.Any(element => string.Equals(element.Name, name, StringComparison.Ordinal))) {
            return;
        }

        MarkupElement closed;
        do {
            closed = open.Pop();
        } while (!string.Equals(closed.Name, name, StringComparison.Ordinal));
    }

    private static int SkipPast(string html, int start, string terminator) {
        int end = html.IndexOf(terminator, start, StringComparison.Ordinal);

        return end < 0 ? html.Length : end + terminator.Length;
    }

    private static void AppendText(Stack<MarkupElement> open, string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return;
        }

        string decoded = WebUtility.HtmlDecode(text);
        foreach (MarkupElement element in open) {
            element.AppendText(decoded);
        }
    }
}
