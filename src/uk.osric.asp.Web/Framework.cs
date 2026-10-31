// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using System.Net;

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

    /// <summary>Renders compiler messages as an HTML block, encoding every message.</summary>
    /// <param name="diagnostics">The messages to show.</param>
    public static string GetDiagnostics(IReadOnlyList<string> diagnostics) {
        return "<pre class=\"diagnostics\">"
            + string.Concat(diagnostics.Select(diagnostic => WebUtility.HtmlEncode(diagnostic) + "\n"))
            + "</pre>";
    }

}




