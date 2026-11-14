// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>One of the site's own routes, as the assistant may read it.</summary>
/// <param name="Path">The canonical key the route owns.</param>
/// <param name="Methods">The methods it answers, as the model should hear them.</param>
/// <param name="Access">Who may reach it, and any throttle or gate.</param>
/// <param name="Description">One line: what the route does.</param>
internal sealed record ShellRoute(string Path, string Methods, string Access, string Description);

/// <summary>What each reserved route answers, for the model.</summary>
/// <remarks>
/// The deployed image carries the assembly, not the source tree, so a shell file is described rather
/// than quoted. Every record is one fact per line with the description never wrapped, so a line
/// number from <c>grep</c> and a range from <c>read_page</c> mean the same thing to a reader and to
/// the model. A test pins the table to <see cref="ReservedPaths.Default"/> in both directions, so a
/// new route cannot be forgotten and a retired one cannot linger.
/// </remarks>
internal static class ShellRoutes {

    /// <summary>The shipped records, in <see cref="ReservedPaths.Default"/> order.</summary>
    public static IReadOnlyList<ShellRoute> All { get; } = [
        new("/admin/changes", "GET", "the owner",
            "The changelist: every commit in the deployed build, newest first, each linking to its page on GitHub."),
        new("/admin/export", "GET, POST", "the owner",
            "Lists the page store's contents and downloads it as a SQLite archive."),
        new("/admin/import", "GET, POST", "the owner",
            "Uploads a page archive, shows what it would change and applies it all or nothing."),
        new("/admin/restore", "GET, POST", "the owner",
            "Restores every page the release ships with from its bundled source, showing what would change and then what it changed."),
        new("/assistant", "GET, POST, HEAD", "the owner; only while Assistant:Enabled is true",
            "The page assistant: conversations, transcript, run stream, tool approvals, the owner's memory list and the pause a run takes when it runs out of budget."),
        new("/admin/scrub", "GET, POST", "the owner",
            "Scrubs a deleted page and every revision it retained, once the page's key is typed to confirm."),
        new("/favicon.ico", "GET", "anyone",
            "Always 404, so no page can claim it; the icon is /rocket.svg, which the layout links."),
        new("/heath", "GET", "anyone",
            "The health probe. The misspelling is deliberate and stays."),
        new("/login", "GET, POST", "anyone, throttled to ten attempts a minute per client address",
            "The sign-in form: verifies a password, mints the session cookie and returns to returnUrl."),
        new("/logout", "GET, POST", "anyone",
            "The sign-out confirmation, then the POST that deletes the stored ticket and the cookie."),
        new("/metrics", "GET", "Prometheus on the internal network; 404 through the public origin",
            "The Prometheus scrape endpoint."),
        new("/status", "GET", "the owner",
            "The status dashboard: metrics, storage and build, rendered by the server without JavaScript."),
    ];

    private const string ShellFileLine =
        "shell file: a route the site answers with its own code, never a stored page";

    private const string Undescribed =
        "A route this build does not describe. The site answers this key, so no page is stored there and none can be saved.";

    /// <summary>The record a read of this key returns.</summary>
    /// <param name="path">A canonical, reserved key.</param>
    /// <remarks>No trailing line break: the description is the last line, so five lines really are five.</remarks>
    public static string Text(PagePath path) {
        ArgumentNullException.ThrowIfNull(path);

        ShellRoute? route = All.FirstOrDefault(item => string.Equals(item.Path, path.Key, StringComparison.Ordinal));
        StringBuilder text = new();
        _ = text.Append(path.Key).Append('\n').Append(ShellFileLine);

        if (route is null) {
            return text.Append("\ndescription: ").Append(Undescribed).ToString();
        }

        return text.Append("\nmethods: ").Append(route.Methods)
            .Append("\naccess: ").Append(route.Access)
            .Append("\ndescription: ").Append(route.Description)
            .ToString();
    }
}
