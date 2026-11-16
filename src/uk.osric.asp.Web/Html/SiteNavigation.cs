// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Html;

using uk.osric.asp.Sdk;

/// <summary>The one way home a shell document carries.</summary>
/// <remarks>
/// <para>
/// The shell's own documents are bootstrap and recovery surfaces, not the site's views: the error,
/// sign-in, status and admin routes have to answer when the page pipeline is broken, so they cannot
/// depend on a stored page. They carry one way home — <c>/</c>, plus <c>/admin</c> at or under that
/// prefix — and nothing else. The site menu a visitor sees day to day is written by the stored
/// <c>/_layout</c>, which is why the two are no longer copies of each other.
/// </para>
/// <para>
/// The footer's two links stay: a footer is where the links every page carries belong, and they are
/// not the menu.
/// </para>
/// </remarks>
internal static class SiteNavigation {

    /// <summary>The label no other navigation landmark may repeat.</summary>
    public const string Label = "Site";

    /// <summary>The class the menu's <c>nav</c> carries, so the sheets can style it.</summary>
    public const string Class = "site-nav";

    /// <summary>The menu for the request being answered.</summary>
    /// <param name="context">The request whose path decides the items.</param>
    public static string For(HttpContext context) {
        ArgumentNullException.ThrowIfNull(context);

        return Nav(context.Request.Path.Value ?? "/");
    }

    /// <summary>The rendered menu for a request path.</summary>
    /// <param name="path">The canonical request path.</param>
    public static string Nav(string path) => ShellDocument.Nav(Label, Links(path), Class);

    /// <summary>The items, in reading order, with the one being shown marked as text.</summary>
    /// <param name="path">The canonical request path.</param>
    public static IReadOnlyList<NavLink> Links(string path) {
        ArgumentNullException.ThrowIfNull(path);

        List<NavLink> links = [
            new("/", "Home", At(path, "/")),
        ];

        if (Under(path, "/admin")) {
            links.Add(new("/admin", "Admin", Under(path, "/admin")));
        }

        return links;
    }

    /// <summary>The items the document's footer carries, which are the same on every page.</summary>
    public static IReadOnlyList<NavLink> FooterLinks() => [
        new("/about", "About", false),
        new("/privacy", "Privacy policy", false),
    ];

    private static bool At(string path, string href) => string.Equals(path, href, StringComparison.Ordinal);

    private static bool Under(string path, string href) =>
        At(path, href) || path.StartsWith(href + "/", StringComparison.Ordinal);
}
