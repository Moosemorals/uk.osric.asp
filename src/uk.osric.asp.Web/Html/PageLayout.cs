// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Html;

using System.Net;
using uk.osric.asp.Core.Compilation;

internal static class PageLayout {

    /// <summary>Renders one number per source line, each an anchor a diagnostic can point at.</summary>
    /// <param name="lineCount">The number of source lines, from <see cref="TemplateLines.Count"/>.</param>
    /// <remarks>
    /// Decorative, so the container is hidden from assistive technology: the textarea already holds
    /// the text, and the diagnostic link says its position in words. No whitespace between the spans,
    /// because one number per row is the whole layout.
    /// </remarks>
    public static string Gutter(int lineCount) =>
        "<div class=\"gutter\" id=\"gutter\" aria-hidden=\"true\">"
        + string.Concat(
            Enumerable.Range(1, lineCount)
                .Select(number => $"<span id=\"{LineAnchor(number)}\">{number}</span>"))
        + "</div>";

    /// <summary>Renders a navigation landmark from its label and links.</summary>
    /// <param name="label">The landmark's accessible name, which no other <c>nav</c> may repeat.</param>
    /// <param name="links">The links, in reading order.</param>
    /// <param name="className">A class for the <c>nav</c>, when the view styles it.</param>
    /// <remarks>The label is required, so an unnamed navigation landmark cannot be written.</remarks>
    public static string Nav(string label, IReadOnlyList<NavLink> links, string? className = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(links);

        string attribute = className is null ? string.Empty : $" class=\"{WebUtility.HtmlEncode(className)}\"";
        string body = string.Join(" ", links.Select(link => link.IsCurrent
            ? $"<span class=\"current\" aria-current=\"true\">{WebUtility.HtmlEncode(link.Text)}</span>"
            : $"<a href=\"{WebUtility.HtmlEncode(link.Href)}\">{WebUtility.HtmlEncode(link.Text)}</a>"));

        return $"<nav{attribute} aria-label=\"{WebUtility.HtmlEncode(label)}\">{body}</nav>";
    }

    /// <summary>Renders diagnostic lines as a list, encoding every message.</summary>
    /// <param name="lines">The lines to show, in the order given.</param>
    /// <param name="linkToLines">Whether a line with a number links to that gutter number.</param>
    /// <remarks>
    /// <c>role="list"</c> is deliberate: the sheet removes the markers, and a list styled that way
    /// keeps its semantics only where the role says so.
    /// </remarks>
    public static string Diagnostics(IReadOnlyList<DiagnosticLine> lines, bool linkToLines = false) {
        ArgumentNullException.ThrowIfNull(lines);

        return "<ul class=\"diagnostics\" role=\"list\">"
            + string.Concat(lines.Select(line => {
                string text = WebUtility.HtmlEncode(line.Text);
                string body = linkToLines && line.Line is { } number
                    ? $"<a href=\"#{LineAnchor(number)}\">{text}</a>"
                    : text;
                return $"<li{SeverityClass(line.Severity)}>{body}</li>";
            }))
            + "</ul>";
    }

    private static string SeverityClass(PageDiagnosticSeverity? severity) => severity switch {
        PageDiagnosticSeverity.Error => " class=\"diagnostic-error\"",
        PageDiagnosticSeverity.Warning => " class=\"diagnostic-warning\"",
        _ => string.Empty,
    };

    private static string LineAnchor(int line) => $"L{line}";
}
