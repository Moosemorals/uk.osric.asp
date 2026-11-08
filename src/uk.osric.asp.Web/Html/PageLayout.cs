// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Html;

using System.Net;
using uk.osric.asp.Core.Compilation;

/// <summary>Renders the editor's source-line gutter.</summary>
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
