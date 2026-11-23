// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace ReferenceCorpus;

using System.Text;
using System.Xml.Linq;

/// <summary>Flattens a documentation element to the text the assistant hears.</summary>
/// <remarks>
/// The page and the tool read one tree; this is the second output, not a second parser. Paragraphs
/// become blank-separated text, lists become lines, and an inline element becomes its own words.
/// </remarks>
internal static class ReferenceText {

    /// <summary>The text of a summary, remarks or other block, with whitespace tidied.</summary>
    /// <param name="element">The element to flatten.</param>
    public static string Flatten(XElement element) {
        ArgumentNullException.ThrowIfNull(element);

        StringBuilder builder = new();
        AppendNode(element, builder, inCode: false);
        return Tidy(builder.ToString());
    }

    private static void AppendNode(XNode node, StringBuilder builder, bool inCode) {
        switch (node) {
            case XText text:
                AppendText(text.Value, builder, inCode);
                break;
            case XElement element:
                AppendElement(element, builder, inCode);
                break;
        }
    }

    private static void AppendElement(XElement element, StringBuilder builder, bool inCode) {
        switch (element.Name.LocalName) {
            case "inheritdoc":
                return;
            case "para":
                NewLine(builder, 2);
                AppendChildren(element, builder, inCode);
                NewLine(builder, 2);
                return;
            case "br":
                NewLine(builder, 1);
                return;
            case "code":
                NewLine(builder, 1);
                AppendChildren(element, builder, inCode: true);
                NewLine(builder, 1);
                return;
            case "see":
                AppendSee(element, builder);
                return;
            case "paramref":
            case "typeparamref":
                builder.Append((string?)element.Attribute("name") ?? string.Empty);
                return;
            case "param":
            case "typeparam":
                AppendChildren(element, builder, inCode);
                return;
            case "list":
                AppendList(element, builder);
                return;
            default:
                AppendChildren(element, builder, inCode);
                return;
        }
    }

    private static void AppendSee(XElement element, StringBuilder builder) {
        if (element.Nodes().Any()) {
            AppendChildren(element, builder, inCode: false);
            return;
        }

        if ((string?)element.Attribute("text") is { Length: > 0 } text) {
            builder.Append(text);
            return;
        }

        if ((string?)element.Attribute("langword") is { Length: > 0 } word) {
            builder.Append(word);
            return;
        }

        if ((string?)element.Attribute("cref") is { Length: > 0 } cref) {
            builder.Append(ReferenceDisplay.CrefText(cref));
            return;
        }

        builder.Append((string?)element.Attribute("href") ?? string.Empty);
    }

    private static void AppendList(XElement list, StringBuilder builder) {
        bool ordered = (string?)list.Attribute("type") is "number";
        int index = 1;
        foreach (XElement item in list.Elements("item")) {
            NewLine(builder, 1);
            builder.Append(ordered
                ? index++.ToString(System.Globalization.CultureInfo.InvariantCulture) + ". "
                : "- ");
            XElement? term = item.Element("term");
            XElement? description = item.Element("description");
            if (term is not null) {
                AppendChildren(term, builder, inCode: false);
                if (description is not null) {
                    builder.Append(" — ");
                }
            }

            if (description is not null) {
                AppendChildren(description, builder, inCode: false);
            } else {
                AppendChildren(item, builder, inCode: false);
            }
        }

        NewLine(builder, 1);
    }

    private static void AppendChildren(XElement element, StringBuilder builder, bool inCode) {
        foreach (XNode child in element.Nodes()) {
            AppendNode(child, builder, inCode);
        }
    }

    private static void AppendText(string value, StringBuilder builder, bool inCode) {
        if (inCode) {
            builder.Append(value);
            return;
        }

        foreach (char character in value) {
            if (!char.IsWhiteSpace(character)) {
                builder.Append(character);
            } else if (builder.Length > 0 && builder[^1] is not ' ' and not '\n') {
                builder.Append(' ');
            }
        }
    }

    private static void NewLine(StringBuilder builder, int count) {
        while (builder.Length > 0 && builder[^1] == ' ') {
            _ = builder.Remove(builder.Length - 1, 1);
        }

        int existing = 0;
        for (int index = builder.Length - 1; index >= 0 && builder[index] == '\n'; index--) {
            existing++;
        }

        for (int index = existing; index < count; index++) {
            builder.Append('\n');
        }
    }

    private static string Tidy(string text) {
        List<string> lines = [];
        bool blank = false;
        foreach (string line in text.Split('\n').Select(item => item.Trim())) {
            if (line.Length == 0) {
                if (lines.Count > 0 && !blank) {
                    lines.Add(string.Empty);
                    blank = true;
                }

                continue;
            }

            lines.Add(line);
            blank = false;
        }

        return string.Join('\n', lines).Trim();
    }
}
