// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using System.Net;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Pages;

/// <summary>The copyright and licence page, read from the files the build carries.</summary>
/// <remarks>
/// Public even though the key is under <c>/admin</c>: the licence of published source, and the
/// notices for the packages the site ships, are for whoever asks. It draws the administration menu
/// because that is where a reader finds it, as the reference page does.
/// </remarks>
internal static class CopyrightEndpoint {

    /// <summary>Answers <c>GET /admin/copyright</c>.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="library">The files this build carries.</param>
    /// <param name="markdown">Renders the third-party notices, which are written in Markdown.</param>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    public static async Task HandleAsync(
        HttpContext context,
        LicenceLibrary library,
        MarkdownRenderer markdown,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(markdown);

        await ShellDocument.BeginAsync(
            context,
            "Copyright and licences",
            SiteNavigation.For(context),
            cancellationToken: cancellationToken);

        await context.Response.WriteAsync("<h1>Copyright and licences</h1>", cancellationToken);
        await context.Response.WriteAsync(
            AdminNavigation.Nav("copyright", EditorPaths.Of(context.RequestServices)),
            cancellationToken);
        await context.Response.WriteAsync(
            """
            <p>Every file in this repository carries its copyright and licence in SPDX tags, and
            <code>make license-check</code> reads them with the REUSE tool. This page is written from the
            files this build carries, so it cannot drift from them.</p>
            <h2 id="site">The site's source</h2>
            """,
            cancellationToken);

        if (library.SiteLicence is { } licence) {
            await context.Response.WriteAsync(
                "<p>Copyright 2026 Osric Wilkinson &lt;<a href=\"mailto:osric@fluffypeople.com\">osric@fluffypeople.com</a>&gt;, "
                + "under the MIT licence.</p>",
                cancellationToken);
            await WriteTextAsync(context, "The MIT licence", "licence-site", licence, cancellationToken);
        } else {
            await context.Response.WriteAsync(
                "<p>This build does not carry its <code>LICENSE</code>, so the site's own terms are in the "
                + "<a href=\"https://github.com/Moosemorals/uk.osric.asp\">repository</a>.</p>",
                cancellationToken);
        }

        // The notice is the file's own Markdown, and its headings are written to sit under this
        // page's h1: the file's title is an h2 and each package an h3, so the two surfaces agree.
        if (library.ThirdPartyNotices is { } notices) {
            await context.Response.WriteAsync(markdown.ToHtml(notices), cancellationToken);
        } else {
            await context.Response.WriteAsync(
                "<h2 id=\"third-party\">Third-party software</h2>"
                + "<p>This build does not carry <code>THIRD-PARTY-NOTICES.md</code>.</p>",
                cancellationToken);
        }

        await context.Response.WriteAsync(
            "<h2 id=\"texts\">The licence texts</h2>",
            cancellationToken);

        if (library.Licences.Count == 0) {
            await context.Response.WriteAsync(
                "<p>This build does not carry its <code>LICENSES</code> folder.</p>",
                cancellationToken);
        } else {
            foreach (LicenceText text in library.Licences) {
                await WriteTextAsync(
                    context,
                    text.Name,
                    "licence-" + text.Name.ToLowerInvariant(),
                    text.Text,
                    cancellationToken);
            }
        }

        await context.Response.WriteAsync(
            """
            <h2 id="reuse">REUSE.toml</h2>
            <p>Some files cannot carry a comment — JSON, the licence text itself, the generated
            migrations and the <code>PublicAPI</code> files an analyzer parses — so this file lists them
            with the tags they would have carried. It is a file of annotations, not another licence.</p>
            """,
            cancellationToken);

        if (library.Reuse is { } reuse) {
            await WriteTextAsync(context, "REUSE.toml", "reuse-file", reuse, cancellationToken);
        } else {
            await context.Response.WriteAsync(
                "<p>This build does not carry <code>REUSE.toml</code>.</p>",
                cancellationToken);
        }

        await context.Response.WriteAsync(
            "<p><a href=\"/about\">About this site</a> · <a href=\"/admin\">Administration</a></p>",
            cancellationToken);

        await ShellDocument.EndAsync(context, cancellationToken);
    }

    // A collapsible text block: the licence is long, and the page's job is to say what applies before
    // it says it at length.
    private static async Task WriteTextAsync(
        HttpContext context,
        string summary,
        string id,
        string text,
        CancellationToken cancellationToken) {
        await context.Response.WriteAsync(
            $"<h3 id=\"{WebUtility.HtmlEncode(id)}\">{WebUtility.HtmlEncode(summary)}</h3>\n"
            + "<details><summary>Read the text</summary>\n<pre class=\"source\" tabindex=\"0\">",
            cancellationToken);
        await context.Response.WriteAsync(WebUtility.HtmlEncode(text), cancellationToken);
        await context.Response.WriteAsync("</pre>\n</details>\n", cancellationToken);
    }
}
