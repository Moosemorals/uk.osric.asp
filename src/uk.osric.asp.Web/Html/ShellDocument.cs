// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Html;

using System.Net;

/// <summary>Writes the document shell every non-page view shares: head, skip link and the open <c>main</c> landmark.</summary>
internal static class ShellDocument {

    /// <summary>Starts a shell document: the head, the skip link and the open <c>main</c> landmark.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="title">The document title, encoded here.</param>
    /// <param name="statusCode">The response status.</param>
    /// <param name="stylesheet">A sheet the view needs, after <c>/site.css</c> and before <c>/layout.css</c>.</param>
    /// <param name="bodyClass">A class for <c>body</c>, when the view styles the document box.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <remarks>
    /// One place owns the document, so a view cannot forget the landmark or the skip link, and
    /// <c>#main</c> always exists. <c>/layout.css</c> is linked last so it can correct a copy of a
    /// seeded sheet that an existing installation still holds: seeds are never overwritten.
    /// </remarks>
    public static Task BeginAsync(
        HttpContext context,
        string title,
        int statusCode = StatusCodes.Status200OK,
        string? stylesheet = null,
        string? bodyClass = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(title);

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.StatusCode = statusCode;
        context.Response.Headers.CacheControl = "no-store";

        return context.Response.WriteAsync($"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>{WebUtility.HtmlEncode(title)}</title>
                <link rel="stylesheet" href="/site.css">
                {(stylesheet is null ? "" : $"<link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(stylesheet)}\">")}
                <link rel="stylesheet" href="/layout.css">
                <link rel="icon" href="/rocket.svg" type="image/svg+xml">
            </head>
            <body{(bodyClass is null ? "" : $" class=\"{WebUtility.HtmlEncode(bodyClass)}\"")}>
            <a class="skip-link" href="#main">Skip to main content</a>
            <main id="main">
            """, cancellationToken);
    }

    /// <summary>Closes the landmark and the document.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public static Task EndAsync(HttpContext context, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);

        return context.Response.WriteAsync($"""
            </main>
            </body>
            </html>
            """, cancellationToken);
    }
}
