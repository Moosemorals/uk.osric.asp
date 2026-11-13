// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Html;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

/// <summary>Writes the document every non-page view shares, through the markup the Sdk owns.</summary>
internal static class ShellDocument {

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
    /// One place owns the response, and <see cref="PageDocument"/> owns the markup, so a view cannot
    /// forget the banner, the skip link or the landmark, and the shell cannot drift from a seeded
    /// layout.
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
        string markup = PageDocument.Head(
                title,
                stylesheets,
                head,
                bodyClass,
                href => PageAssetMap.Resolve(assets, href))
            + PageDocument.Banner(navigation)
            + PageDocument.OpenMain();

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

        return context.Response.WriteAsync(PageDocument.CloseMain() + PageDocument.End(), cancellationToken);
    }
}
