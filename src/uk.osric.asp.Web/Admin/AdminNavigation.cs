// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;

/// <summary>The navigation landmark the administration views share.</summary>
internal static class AdminNavigation {

    /// <summary>The label no other navigation landmark may repeat.</summary>
    public const string Label = "Site administration";

    /// <summary>Renders the administration tools the shell offers.</summary>
    /// <param name="current">The view being shown, so its own link is marked rather than repeated.</param>
    /// <param name="editorPath">The editor's own key, which is configuration.</param>
    public static string Nav(string current, string editorPath) {
        // The token is the view's own name, and the index is the one route named for its menu rather
        // than beneath it. Deriving the href keeps one list rather than a second copy of the routes.
        string currentHref = string.Equals(current, "index", StringComparison.Ordinal)
            ? "/admin"
            : "/admin/" + current;

        List<NavLink> links = [];
        foreach (ShellLink link in ShellLinks.All("/", string.Empty, editorPath)) {
            // The administration views are where the owner arrived, so the whole section is drawn;
            // the index is the one reader that hides what this visitor cannot reach.
            if (link.Section == ShellSection.Administration) {
                links.Add(new(link.Href, link.Text, string.Equals(link.Href, currentHref, StringComparison.Ordinal)));
            }
        }

        return ShellDocument.Nav(Label, links, "editor-menu");
    }
}
