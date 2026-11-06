// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Html;

using System.Net;

/// <summary>Writes the document shell every non-page view shares: the head and the open <c>main</c> landmark.</summary>
internal static class ShellDocument {

    /// <summary>Starts a shell document: the head and the open <c>main</c> landmark.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="title">The document title, encoded here.</param>
    /// <param name="statusCode">The response status.</param>
    /// <param name="stylesheet">A sheet the view needs, after <c>/layout.css</c> and <c>/site.css</c>.</param>
    /// <param name="bodyClass">A class for <c>body</c>, when the view styles the document box.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <remarks>
    /// One place owns the document, so a view cannot forget the landmark, and <c>#main</c> always
    /// exists. Nothing precedes it in the body, which is why there is no skip link: with no banner
    /// or site navigation to pass, the link would target the element immediately after it
    /// (DEC-121). <c>/layout.css</c> is linked first: it carries the document's shape, and
    /// <c>/site.css</c> and the view's own sheet come after it so a view can override a structural
    /// rule without a more specific selector.
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
        // A view that mints an antiforgery token already carries the framework's no-cache, no-store;
        // this weaker value would replace it, and setting one first makes the framework log a warning.
        if (context.Response.Headers.CacheControl.Count == 0) {
            context.Response.Headers.CacheControl = "no-store";
        }

        return context.Response.WriteAsync($"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>{WebUtility.HtmlEncode(title)}</title>
                <link rel="stylesheet" href="/layout.css">
                <link rel="stylesheet" href="/site.css">
                {(stylesheet is null ? "" : $"<link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(stylesheet)}\">")}
                <link rel="icon" href="/rocket.svg" type="image/svg+xml">
            </head>
            <body{(bodyClass is null ? "" : $" class=\"{WebUtility.HtmlEncode(bodyClass)}\"")}>
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
