// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Shell;

using Microsoft.AspNetCore.WebUtilities;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>The site's own routes, and the menus they appear in, in one table.</summary>
/// <remarks>
/// <para>
/// Four things read this list: the key set a page may not claim (<see cref="Reserved"/>), the record
/// the assistant is given when it reads a shell file (<see cref="Text"/>), the menus the shell draws
/// (<see cref="Links"/>) and the same links given to a page through
/// <see cref="uk.osric.asp.Sdk.IPageCatalog.ShellLinks"/>. They were three hand-written copies — and
/// the assistant's prompt a fourth — so the table is the only place a route is described.
/// </para>
/// <para>
/// A route is a key the site's own code answers. A page link is a key a stored page answers — Home,
/// the editor and the manual — and it is in the table because it is in a menu, not because the shell
/// answers it. The editor's key is configuration, so its entry names a token rather than a path.
/// </para>
/// </remarks>
internal static class ShellCatalogue {

    private const string ShellFileLine =
        "shell file: a route the site answers with its own code, never a stored page";

    private const string Undescribed =
        "A route this build does not describe. The site answers this key, so no page is stored there and none can be saved.";

    /// <summary>Every entry, in the order a menu reads them when its numbers tie.</summary>
    public static IReadOnlyList<ShellEntry> Entries { get; } = [
        PageLink(ShellPage.Home, ShellAudience.Everyone, ShellSection.Site, 10, "Home", ShellMatch.Leaf),

        // Site: the way in and the way out, then the owner's tools.
        new ShellRoute("/login", "GET, POST", ShellAudience.Everyone,
            "The sign-in form: verifies a password, mints the session cookie and returns to returnUrl.",
            new ShellMenu(ShellSection.Site, ShellAudience.Anonymous, 20, "Sign in", ShellMatch.Leaf)) {
            AccessNote = "anyone, throttled to ten attempts a minute per client address",
        },
        new ShellRoute("/assistant", "GET, POST, HEAD", ShellAudience.Owner,
            "The page assistant: conversations, transcript, run stream, tool approvals, the owner's answers to the assistant's questions, the owner's memory list and the pause a run takes when it runs out of budget.",
            new ShellMenu(ShellSection.Site, ShellAudience.Owner, 30, "Assistant", ShellMatch.Subtree)),
        PageLink(ShellPage.Editor, ShellAudience.Owner, ShellSection.Site, 40, "Pages", ShellMatch.Subtree),
        new ShellRoute("/admin", "GET", ShellAudience.Everyone,
            "The administration index: every tool the shell offers that this visitor may reach, drawn from one list of shell links.",
            new ShellMenu(ShellSection.Site, ShellAudience.Owner, 50, "Admin", ShellMatch.Subtree),
            new ShellMenu(ShellSection.Administration, ShellAudience.Everyone, 10, "Administration", ShellMatch.Leaf)),
        new ShellRoute("/logout", "GET, POST", ShellAudience.Owner,
            "The sign-out confirmation, then the POST that deletes the stored ticket and the cookie.",
            new ShellMenu(ShellSection.Site, ShellAudience.Owner, 60, "Sign out", ShellMatch.Leaf)),

        // Administration: the tools, in the order the owner is likely to want them.
        new ShellRoute("/admin/import", "GET, POST", ShellAudience.Owner,
            "Uploads a page archive, shows what it would change and applies it all or nothing.",
            new ShellMenu(ShellSection.Administration, ShellAudience.Owner, 20, "Import pages", ShellMatch.Subtree)),
        new ShellRoute("/admin/export", "GET, POST", ShellAudience.Owner,
            "Lists the page store's contents and downloads it as a SQLite archive.",
            new ShellMenu(ShellSection.Administration, ShellAudience.Owner, 30, "Export pages", ShellMatch.Subtree)),
        new ShellRoute("/admin/changes", "GET", ShellAudience.Owner,
            "The changelist: every commit in the deployed build, newest first, each linking to its page on GitHub.",
            new ShellMenu(ShellSection.Administration, ShellAudience.Owner, 40, "Changelist", ShellMatch.Leaf)),
        new ShellRoute("/admin/seeds", "GET, POST", ShellAudience.Owner,
            "The seed page: every page this release ships with a tick per row the owner chooses, the diff of what a write would change, and the stored rows at keys the shell answers, scrubbed after a typed confirmation.",
            new ShellMenu(ShellSection.Administration, ShellAudience.Owner, 50, "Seeds", ShellMatch.Leaf)),
        new ShellRoute("/admin/reference", "GET", ShellAudience.Everyone,
            "The documentation this build carries: one page per product assembly, with every type and member. The page contract's own words are read with the reference tool.",
            new ShellMenu(ShellSection.Administration, ShellAudience.Everyone, 70, "Reference", ShellMatch.Subtree)),
        new ShellRoute("/admin/copyright", "GET", ShellAudience.Everyone,
            "The copyright and licence page: the site's licence, the third-party notices and the LICENSES texts, read from the files the image carries.",
            new ShellMenu(ShellSection.Administration, ShellAudience.Everyone, 80, "Copyright and licences", ShellMatch.Leaf)),
        new ShellRoute("/admin/status", "GET", ShellAudience.Owner,
            "The status dashboard: metrics, storage and build, rendered by the server without JavaScript.",
            new ShellMenu(ShellSection.Administration, ShellAudience.Owner, 90, "Status", ShellMatch.Subtree)),
        PageLink(ShellPage.Editor, ShellAudience.Owner, ShellSection.Administration, 100, "Page list", ShellMatch.Subtree),
        PageLink(ShellPage.Help, ShellAudience.Everyone, ShellSection.Administration, 110, "Help", ShellMatch.Subtree),

        // The shell's own files: answered by the web root, so they are reserved but never a menu.
        new ShellRoute("/admin.css", "GET", ShellAudience.Everyone,
            "The administration views' sheet, served from the web root: the selection list and its tags."),
        new ShellRoute("/admin.js", "GET", ShellAudience.Everyone,
            "The administration views' script, served from the web root: the import and export selection counter and its buttons."),
        new ShellRoute("/reference.css", "GET", ShellAudience.Everyone,
            "The reference page's sheet, served from the web root: the type index, signatures and parameter lists."),
        new ShellRoute("/rocket.svg", "GET", ShellAudience.Everyone,
            "The site's icon, ISC licensed, served from the web root: every document links it and /about names its licence."),
        new ShellRoute("/status.css", "GET", ShellAudience.Everyone,
            "The status dashboard's sheet, served from the web root: the banner, the window nav and the panels."),

        // A stored object, by digest. The entry reserves the whole /o prefix, so no page can claim a
        // path beneath it, and tells the assistant what the route is.
        new ShellRoute("/o", "GET, HEAD", ShellAudience.Everyone,
            "A stored object by its SHA-256 digest: a page's object is public and immutable, a visitor's private and uncacheable, with ETag, Range and HEAD."),

        // Served by Caddy from the host's CI status directory, not by this app: the entry reserves the
        // key so a stored page cannot shadow it, and tells the assistant what the path is.
        new ShellRoute("/ci/status.json", "GET", ShellAudience.Everyone,
            "The host's CI status: the commit the last trunk push checked, when it ran and whether it passed. Caddy serves it; the app does not answer this path."),

        // Routes with no menu: reached from a confirmation, or not reached at all.
        new ShellRoute("/admin/scrub", "GET, POST", ShellAudience.Owner,
            "Scrubs a deleted page and every revision it retained, once the page's key is typed to confirm."),
        new ShellRoute("/favicon.ico", "GET", ShellAudience.Everyone,
            "Always 404, so no page can claim it; the icon is /rocket.svg, which the layout links."),
        new ShellRoute("/health", "GET", ShellAudience.Everyone,
            "The health probe: storage reachability and the count of abandoned renders."),
        new ShellRoute("/metrics", "GET", ShellAudience.Everyone,
            "The Prometheus scrape endpoint.") {
            AccessNote = "Prometheus on the internal network; 404 through the public origin",
        },
    ];

    /// <summary>Every route the site's own code answers, in table order.</summary>
    public static IReadOnlyList<ShellRoute> Routes { get; } = [.. Entries.OfType<ShellRoute>()];

    /// <summary>The keys no page may claim, in table order.</summary>
    public static IReadOnlyList<string> Reserved { get; } = [.. Routes.Select(route => route.Key)];

    /// <summary>The shell's links as values, the Site menu first and each section in its reading order.</summary>
    /// <param name="path">The request's path, which the sign-in link returns to.</param>
    /// <param name="query">The request's raw query, carried into the sign-in link.</param>
    /// <param name="editorPath">The editor's own key, which is configuration.</param>
    public static IReadOnlyList<ShellLink> Links(string path, string query, string editorPath) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(editorPath);

        return [
            .. Section(ShellSection.Site, path, query, editorPath),
            .. Section(ShellSection.Administration, path, query, editorPath),
        ];
    }

    /// <summary>Whether a visitor with this identity may follow the link.</summary>
    /// <param name="link">The link to test.</param>
    /// <param name="authenticated">Whether the visitor has signed in.</param>
    /// <remarks>
    /// A link to a route that answers only a signed-in visitor is hidden from everyone else, and the
    /// sign-in link is the one thing an owner does not need.
    /// </remarks>
    public static bool IsFor(ShellLink link, bool authenticated) => link.Audience switch {
        ShellAudience.Everyone => true,
        ShellAudience.Anonymous => !authenticated,
        _ => authenticated,
    };

    /// <summary>Whether the request path is the place this link leads to.</summary>
    /// <param name="link">The link to test.</param>
    /// <param name="path">The canonical request path.</param>
    /// <remarks>
    /// The sign-in link carries the way back as a query, so its current check is the path it leads to
    /// rather than the href it was given. The match is the link's own: a tool is current beneath its
    /// own key, and a view is current only at it.
    /// </remarks>
    public static bool IsCurrent(ShellLink link, string path) {
        ArgumentNullException.ThrowIfNull(path);

        int query = link.Href.IndexOf('?', StringComparison.Ordinal);
        string target = query >= 0 ? link.Href[..query] : link.Href;

        return string.Equals(path, target, StringComparison.Ordinal)
            || (link.Match == ShellMatch.Subtree && path.StartsWith(target + "/", StringComparison.Ordinal));
    }

    /// <summary>The record a read of this key returns.</summary>
    /// <param name="path">A canonical key.</param>
    /// <remarks>
    /// No trailing line break: the description is the last line, so five lines really are five. A key
    /// the table does not describe still answers, with the one sentence that says so, because the
    /// assistant reads a key it was told is a shell file and a shape it did not expect is worse than
    /// a record that admits it knows nothing.
    /// </remarks>
    public static string Text(PagePath path) {
        ArgumentNullException.ThrowIfNull(path);

        ShellRoute? route = Routes.FirstOrDefault(item => string.Equals(item.Key, path.Key, StringComparison.Ordinal));

        return route is null
            ? $"{path.Key}\n{ShellFileLine}\ndescription: {Undescribed}"
            : Record(route);
    }

    private static IReadOnlyList<ShellLink> Section(ShellSection section, string path, string query, string editorPath) => [
        .. Entries
            .SelectMany(entry => entry.Menus
                .Where(menu => menu.Section == section)
                .Select(menu => (Entry: entry, Menu: menu)))
            .OrderBy(item => item.Menu.Order)
            .Select(item => new ShellLink(
                Href(item.Entry, path, query, editorPath),
                item.Menu.Text,
                section,
                item.Menu.Audience,
                item.Menu.Match)),
    ];

    private static string Href(ShellEntry entry, string path, string query, string editorPath) {
        if (entry is ShellRoute { Key: "/login" }) {
            // The one href that is not its key: the sign-in form carries the way back, so a visitor
            // lands where they were going rather than on the editor.
            return "/login" + QueryString.Create("returnUrl", path + query).ToUriComponent();
        }

        return entry.Href(editorPath);
    }

    private static ShellPageLink PageLink(
        ShellPage page,
        ShellAudience audience,
        ShellSection section,
        int order,
        string text,
        ShellMatch match) => new(page, new ShellMenu(section, audience, order, text, match));

    private static string Record(ShellRoute route) =>
        $"{route.Key}\n{ShellFileLine}"
        + $"\nmethods: {route.Methods}"
        + $"\naccess: {route.Access}"
        + $"\ndescription: {route.Description}";
}

/// <summary>One appearance of an entry in a menu.</summary>
/// <param name="Section">The menu it appears in.</param>
/// <param name="Audience">Who may follow it, which is not the route's audience: the sign-in form answers
/// anyone, and its link is for the visitor who has not signed in.</param>
/// <param name="Order">Its place in that menu; the sections are numbered independently.</param>
/// <param name="Text">What it says, which the menu renders as the link's text.</param>
/// <param name="Match">How a request path marks it current.</param>
internal sealed record ShellMenu(ShellSection Section, ShellAudience Audience, int Order, string Text, ShellMatch Match);

/// <summary>Something the shell draws a link to: a route it answers, or a page the store holds.</summary>
/// <param name="Menus">The menus it appears in, if any.</param>
internal abstract record ShellEntry(ShellMenu[] Menus) {
    /// <summary>The href the entry is linked by.</summary>
    /// <param name="editorPath">The editor's own key, which is configuration.</param>
    public abstract string Href(string editorPath);
}

/// <summary>A route the site's own code answers, and the record the assistant reads.</summary>
/// <param name="Key">The canonical key the route owns.</param>
/// <param name="Methods">The methods it answers, as the model should hear them.</param>
/// <param name="Audience">Who may reach it.</param>
/// <param name="Description">One line: what the route does.</param>
/// <param name="Menus">The menus it appears in, if any.</param>
internal sealed record ShellRoute(
    string Key,
    string Methods,
    ShellAudience Audience,
    string Description,
    params ShellMenu[] Menus) : ShellEntry(Menus) {

    /// <summary>The sentence <see cref="Access"/> gives instead of the derived one, for a throttle or a gate.</summary>
    public string? AccessNote { get; init; }

    /// <summary>Who may reach it, in the model's words; derived from the audience unless it says more.</summary>
    public string Access => AccessNote ?? Audience switch {
        ShellAudience.Everyone => "anyone",
        ShellAudience.Anonymous => "anyone who has not signed in",
        _ => "the owner",
    };

    /// <inheritdoc/>
    public override string Href(string editorPath) => Key;
}

/// <summary>A page the store holds, which a menu links to as if it were the shell's.</summary>
/// <param name="Page">Which page, as a token, because the editor's key is configuration.</param>
/// <param name="Menus">The menus it appears in.</param>
internal sealed record ShellPageLink(
    ShellPage Page,
    params ShellMenu[] Menus) : ShellEntry(Menus) {

    /// <inheritdoc/>
    public override string Href(string editorPath) => Page switch {
        ShellPage.Home => "/",
        ShellPage.Editor => editorPath,
        _ => "/help",
    };
}

/// <summary>A stored page a menu links to.</summary>
internal enum ShellPage {
    /// <summary>The site's home page, which is a page like any other.</summary>
    Home,

    /// <summary>The editor, whose key is <c>Pages:Editor:Key</c>.</summary>
    Editor,

    /// <summary>The author manual.</summary>
    Help,
}
