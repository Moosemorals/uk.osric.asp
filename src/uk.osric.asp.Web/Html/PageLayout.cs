// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Html;

using System.Net;
using uk.osric.asp.Core.Compilation;

internal static class PageLayout {

    public static string Header(string title, string? stylesheet = null, string? bodyClass = null) =>
        $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>{WebUtility.HtmlEncode(title)}</title>
            <link rel="stylesheet" href="/site.css">
            {(stylesheet is null ? "" : $"<link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(stylesheet)}\">")}
            <link rel="icon" href="/rocket.svg" type="image/svg+xml">
        </head>
        <body{(bodyClass is null ? "" : $" class=\"{WebUtility.HtmlEncode(bodyClass)}\"")}>
        """;

    public static string Footer() =>
        $"""
        </body>
        </html>
        """;

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

    /// <summary>Renders compiler diagnostics as an HTML block, encoding every message.</summary>
    /// <param name="diagnostics">The diagnostics to show, ordered for display.</param>
    /// <param name="linkToLines">Whether a diagnostic with a position links to its gutter number.</param>
    public static string Diagnostics(IReadOnlyList<PageDiagnostic> diagnostics, bool linkToLines = false) {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return "<pre class=\"diagnostics\">"
            + string.Concat(PageDiagnostic.SortedForDisplay(diagnostics).Select(diagnostic => {
                string severity = diagnostic.Severity == PageDiagnosticSeverity.Error
                    ? "diagnostic-error"
                    : "diagnostic-warning";
                string text = WebUtility.HtmlEncode(diagnostic.ToString());
                string body = linkToLines && diagnostic.Position is { } position
                    ? $"<a href=\"#{LineAnchor(position.Line)}\">{text}</a>"
                    : text;
                return $"<span class=\"{severity}\">{body}</span>\n";
            }))
            + "</pre>";
    }

    private static string LineAnchor(int line) => $"L{line}";
}
