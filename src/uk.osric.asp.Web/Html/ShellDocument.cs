// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Html;

using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

/// <summary>Writes the document every shell view is served in, and the pieces it is built from.</summary>
/// <remarks>
/// <para>
/// The shell owns this document because the error, sign-in, status and admin routes must answer when
/// the page pipeline is broken, so it cannot depend on a stored page. The stored layout writes its
/// own document, through the <c>Site.Document</c> library the site seeds; the two are kept honest by
/// a test that compares their shape rather than by sharing a builder.
/// </para>
/// <para>
/// The fragments here still own the document's policy — the language, the charset, the sheet order,
/// the icon, the skip link, the landmark and the close — so a view cannot forget one. They own no
/// <em>response</em> policy: status, content type, security headers, caching and the body cap stay in
/// <see cref="BeginAsync"/>.
/// </para>
/// <para>
/// Every builder returns markup rather than writing it, so the shell writes it to its own response.
/// Encoding happens here: the title and every attribute are escaped, so a caller passes raw values.
/// </para>
/// </remarks>
internal static class ShellDocument {

    /// <summary>The text of the link that skips the chrome.</summary>
    public const string SkipLinkText = "Skip to content";

    /// <summary>The sheets every document links before its own, in this order.</summary>
    /// <remarks><c>/site.css</c> carries the document's shape and the site's look, so a page sheet
    /// overrides a structural rule without a more specific selector.</remarks>
    public static IReadOnlyList<string> BaseStylesheets { get; } = ["/site.css"];

    /// <summary>Whether a value is a path on this site rather than an absolute or protocol-relative URL.</summary>
    /// <param name="href">The value to test.</param>
    /// <remarks>
    /// A sheet or a redirect has to stay on this origin: the site's own policy answers with
    /// <c>style-src 'self'</c>, so an off-site sheet would be blocked silently rather than fail.
    /// </remarks>
    public static bool IsSiteRelative(string? href) {
        if (string.IsNullOrEmpty(href) || href[0] != '/') {
            return false;
        }

        // A second slash or a backslash lets a browser interpret the value as an authority.
        if (href.Length > 1 && (href[1] == '/' || href[1] == '\\')) {
            return false;
        }

        foreach (char character in href) {
            if (character == '\\' || char.IsControl(character)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>Renders the doctype, the head and the open <c>body</c>.</summary>
    /// <param name="title">The document title, encoded here; it may not be blank.</param>
    /// <param name="stylesheets">
    /// The view's own sheets, in the order it asked for them, linked after
    /// <see cref="BaseStylesheets"/>. An exact duplicate is linked once.
    /// </param>
    /// <param name="head">Raw markup written into <c>head</c> last, for a document that describes itself.</param>
    /// <param name="bodyClass">A class for <c>body</c>, when the view styles the document box.</param>
    /// <param name="resolveHref">
    /// Resolves each sheet and the icon before it is written, or <see langword="null"/> to write the
    /// values as given. The caller owns the resolver, so this stays a pure string builder.
    /// </param>
    /// <exception cref="ArgumentException">The title is blank or a sheet is not site-relative.</exception>
    public static string Head(
        string title,
        IReadOnlyList<string>? stylesheets = null,
        string? head = null,
        string? bodyClass = null,
        Func<string, string>? resolveHref = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        IReadOnlyList<string> requested = stylesheets ?? [];
        List<string> sheets = [.. Deduplicated([.. BaseStylesheets, .. requested])];

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>{WebUtility.HtmlEncode(title)}</title>
            {string.Join('\n', sheets.Select(href => Sheet(Resolve(resolveHref, href))))}
            <link rel="icon" href="{WebUtility.HtmlEncode(Resolve(resolveHref, "/rocket.svg"))}" type="image/svg+xml">
            {head}
            </head>
            <body{(bodyClass is null ? "" : $" class=\"{WebUtility.HtmlEncode(bodyClass)}\"")}>
            """;
    }

    /// <summary>Writes the skip link and the banner around a navigation landmark.</summary>
    /// <param name="navigation">The nav markup, normally from <see cref="Nav"/>.</param>
    /// <remarks>
    /// The skip link is the first focusable thing in the body and points at the one <c>main</c>, so it
    /// is a property of the document rather than a habit of each caller.
    /// </remarks>
    public static string Banner(string navigation) {
        ArgumentNullException.ThrowIfNull(navigation);

        return $"""
            <a class="skip-link" href="#main">{WebUtility.HtmlEncode(SkipLinkText)}</a>
            <header class="site-header">
            {navigation}
            </header>
            """;
    }

    /// <summary>Renders a navigation landmark from its label and links.</summary>
    /// <param name="label">The landmark's accessible name, which no other <c>nav</c> may repeat.</param>
    /// <param name="links">The links, in reading order.</param>
    /// <param name="className">A class for the <c>nav</c>, when the view styles it.</param>
    /// <exception cref="ArgumentException">The label is blank.</exception>
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

    /// <summary>Opens the one <c>main</c> landmark, which the skip link targets.</summary>
    public static string OpenMain() => "<main id=\"main\">";

    /// <summary>Closes the landmark <see cref="OpenMain"/> opened.</summary>
    public static string CloseMain() => "</main>";

    /// <summary>Closes the document.</summary>
    public static string End() => """
        </body>
        </html>
        """;

    /// <summary>Starts a shell document: the head, the banner and the open <c>main</c> landmark.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="title">The document title, encoded by the builder.</param>
    /// <param name="navigation">
    /// The site menu, normally <see cref="SiteNavigation.For"/>. It is required so a view cannot be
    /// written without one.
    /// </param>
    /// <param name="statusCode">The response status.</param>
    /// <param name="stylesheets">
    /// The sheets the view needs, after <c>/site.css</c>, in the order given.
    /// </param>
    /// <param name="bodyClass">A class for <c>body</c>, when the view styles the document box.</param>
    /// <param name="head">
    /// Raw markup written into <c>head</c> after the stylesheets, for a document that describes
    /// itself — a progress page's refresh is the only user. It is the caller's markup, not a value
    /// from a request, so nothing needs encoding.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <remarks>
    /// One place owns the response, and the fragments above own the markup, so a view cannot forget
    /// the banner, the skip link or the landmark.
    /// </remarks>
    public static async Task BeginAsync(
        HttpContext context,
        string title,
        string navigation,
        int statusCode = StatusCodes.Status200OK,
        IReadOnlyList<string>? stylesheets = null,
        string? bodyClass = null,
        string? head = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(navigation);

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.StatusCode = statusCode;
        // A view that mints an antiforgery token already carries the framework's no-cache, no-store;
        // this weaker value would replace it, and setting one first makes the framework log a warning.
        SecurityHeaders.NoStoreIfAbsent(context);

        IReadOnlyDictionary<string, string> assets = await AssetMapAsync(context, cancellationToken);
        string markup = Head(
                title,
                stylesheets,
                head,
                bodyClass,
                href => PageAssetMap.Resolve(assets, href))
            + Banner(navigation)
            + OpenMain();

        await context.Response.WriteAsync(markup, cancellationToken);
    }

    /// <summary>Versions a link one of the shell's own views writes in its head markup.</summary>
    /// <param name="context">The request whose asset map the link resolves against.</param>
    /// <param name="href">The site-relative link, such as a script's path.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The versioned URL, or the link unchanged when it is not a live asset.</returns>
    public static async Task<string> AssetUrlAsync(
        HttpContext context,
        string href,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(href);

        IReadOnlyDictionary<string, string> assets = await AssetMapAsync(context, cancellationToken);

        return PageAssetMap.Resolve(assets, href);
    }

    private static Task<IReadOnlyDictionary<string, string>> AssetMapAsync(
        HttpContext context,
        CancellationToken cancellationToken) {
        IPageReadStore? store = context.RequestServices?.GetService<IPageReadStore>();
        PageReadOptions? options = context.RequestServices?.GetService<PageReadOptions>();

        return store is null || options is null
            ? Task.FromResult(PageAssetMap.Empty)
            : PageAssetMap.ForRequestAsync(context, store, options.MaxRowsPerList, cancellationToken);
    }

    /// <summary>Closes the landmark and the document.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public static Task EndAsync(HttpContext context, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);

        return context.Response.WriteAsync(CloseMain() + End(), cancellationToken);
    }

    // Order is the cascade, so a page is the last word on how it looks: an exact duplicate is
    // linked once, and anything that is not on this site is refused rather than blocked silently.
    private static IEnumerable<string> Deduplicated(IReadOnlyList<string> stylesheets) {
        HashSet<string> seen = [];

        foreach (string href in stylesheets) {
            if (!IsSiteRelative(href)) {
                throw new ArgumentException($"'{href}' is not a site-relative URL.", nameof(stylesheets));
            }

            if (seen.Add(href)) {
                yield return href;
            }
        }
    }

    private static string Resolve(Func<string, string>? resolveHref, string href) =>
        resolveHref is null ? href : resolveHref(href);

    private static string Sheet(string href) => $"<link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(href)}\">";
}
