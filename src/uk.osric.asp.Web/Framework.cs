// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using System.Net;
using uk.osric.asp.Core;

public static class Framework {

    public static string GetHeader(string title) =>
        $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>{WebUtility.HtmlEncode(title)}</title>
            <link rel="stylesheet" href="/site.css">
        </head>
        <body>
        """;

    public static string GetFooter() =>
        $"""
        </body>
        </html>
        """;

    /// <summary>Renders compiler diagnostics as an HTML block, encoding every message.</summary>
    /// <param name="diagnostics">The diagnostics to show, in the order the compiler reported them.</param>
    public static string GetDiagnostics(IReadOnlyList<PageDiagnostic> diagnostics) {
        return "<pre class=\"diagnostics\">"
            + string.Concat(PageDiagnostic.SortedForDisplay(diagnostics).Select(diagnostic => {
                string severity = diagnostic.Severity == PageDiagnosticSeverity.Error
                    ? "diagnostic-error"
                    : "diagnostic-warning";
                return $"<span class=\"{severity}\">{WebUtility.HtmlEncode(diagnostic.ToString())}</span>\n";
            }))
            + "</pre>";
    }

}




