// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using System.Net;
using uk.osric.asp.Web.Http;

/// <summary>The bare document the two routine seed pages are written in.</summary>
/// <remarks>
/// These two are the way back when the site is broken, so they cannot be rendered through a stored
/// layout or the page pipeline: their document is written here, with no stylesheet and no navigation
/// beyond the way back to the shell's own pages. Every other admin page is a shell document.
/// </remarks>
internal static class AdminPage {

    /// <summary>The links back to the shell's own pages, written under the heading.</summary>
    public const string BackLinks =
        """<p><a href="/admin">Administration</a> · <a href="/status">Status</a></p>""";

    /// <summary>Writes the document's head and opens its main element.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="title">The document's title, escaped on the way out.</param>
    public static async Task BeginAsync(HttpContext context, string title) {
        context.Response.ContentType = "text/html; charset=utf-8";
        SecurityHeaders.NoStoreIfAbsent(context);

        await context.Response.WriteAsync($"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{WebUtility.HtmlEncode(title)}</title>
            </head>
            <body>
            <a href="#main">Skip to content</a>
            <main id="main">
            """, context.RequestAborted);
    }

    /// <summary>Writes the document's closing markup.</summary>
    /// <param name="context">The request being answered.</param>
    /// <returns>The write, which the caller awaits last.</returns>
    public static Task EndAsync(HttpContext context) => context.Response.WriteAsync("""
        </main>
        </body>
        </html>
        """, context.RequestAborted);
}
