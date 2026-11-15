// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Html;

using Microsoft.AspNetCore.WebUtilities;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Pages;

/// <summary>The site menu the shell's own views carry.</summary>
/// <remarks>
/// <para>
/// The seeded <c>/_layout</c> writes the same menu for stored pages, from the same list of tools, so
/// the two copies are deliberate: the shell's document must not depend on a stored page, because the
/// error, health, sign-in and editor routes have to answer when the page pipeline is broken.
/// </para>
/// <para>
/// The layout links the assistant unconditionally; only the shell knows the raw query a sign-in must
/// return to, and both land somewhere that carries this menu.
/// </para>
/// </remarks>
internal static class SiteNavigation {

    /// <summary>The label no other navigation landmark may repeat.</summary>
    public const string Label = "Site";

    /// <summary>The class the menu's <c>nav</c> carries, so the sheets can style it.</summary>
    public const string Class = "site-nav";

    /// <summary>The menu for the request being answered.</summary>
    /// <param name="context">The request whose principal and path decide the items.</param>
    public static string For(HttpContext context) {
        ArgumentNullException.ThrowIfNull(context);

        bool authenticated = context.User.Identity?.IsAuthenticated == true;

        return Nav(
            authenticated,
            context.Request.Path.Value ?? "/",
            context.Request.QueryString.Value ?? string.Empty,
            EditorPaths.Of(context.RequestServices));
    }

    /// <summary>The rendered menu, from the facts <see cref="For"/> would have read.</summary>
    /// <param name="authenticated">Whether the visitor has signed in.</param>
    /// <param name="path">The canonical request path.</param>
    /// <param name="query">The raw query, carried into the sign-in link.</param>
    /// <param name="editorPath">The editor's own key, which is configuration.</param>
    public static string Nav(bool authenticated, string path, string query, string editorPath) =>
        ShellDocument.Nav(Label, Links(authenticated, path, query, editorPath), Class);

    /// <summary>The items, in reading order, with the one being shown marked as text.</summary>
    /// <param name="authenticated">Whether the visitor has signed in.</param>
    /// <param name="path">The canonical request path.</param>
    /// <param name="query">The raw query, carried into the sign-in link.</param>
    /// <param name="editorPath">The editor's own key, which is configuration.</param>
    public static IReadOnlyList<NavLink> Links(bool authenticated, string path, string query, string editorPath) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(editorPath);

        List<NavLink> links = [
            new("/", "Home", At(path, "/")),
        ];

        if (!authenticated) {
            links.Add(new(SignIn(path, query), "Sign in", At(path, "/login")));
            return links;
        }

        links.Add(new("/assistant", "Assistant", Under(path, "/assistant")));
        links.Add(new(editorPath, "Pages", Under(path, editorPath)));
        links.Add(new("/admin", "Admin", Under(path, "/admin")));
        links.Add(new("/logout", "Sign out", At(path, "/logout")));

        return links;
    }

    /// <summary>The items the document's footer carries, which are the same on every page.</summary>
    /// <remarks>
    /// The layout the stored pages are served in writes these in a footer, and the shell writes the
    /// same document for its own views: a footer is where the two links every page carries belong, and
    /// the header is for where the visitor can go from here.
    /// </remarks>
    public static IReadOnlyList<NavLink> FooterLinks() => [
        new("/about", "About", false),
        new("/privacy", "Privacy policy", false),
    ];

    // The editor the visitor lands on after signing in carries the menu, so a page's own return path
    // only has to survive as a query parameter, not as an absolute URL.
    private static string SignIn(string path, string query) =>
        "/login" + QueryString.Create("returnUrl", path + query).ToUriComponent();

    private static bool At(string path, string href) => string.Equals(path, href, StringComparison.Ordinal);

    private static bool Under(string path, string href) =>
        At(path, href) || path.StartsWith(href + "/", StringComparison.Ordinal);
}
