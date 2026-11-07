// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using uk.osric.asp.Web.Html;

/// <summary>The navigation landmark the two administration views share.</summary>
internal static class AdminNavigation {

    /// <summary>The label no other navigation landmark may repeat.</summary>
    public const string Label = "Page administration";

    /// <summary>Renders the links to both views and back to the page list.</summary>
    /// <param name="current">The view being shown, so its own link is marked rather than repeated.</param>
    public static string Nav(string current) {
        List<NavLink> links = [
            new("/admin/import", "Import pages", string.Equals(current, "import", StringComparison.Ordinal)),
            new("/admin/export", "Export pages", string.Equals(current, "export", StringComparison.Ordinal)),
            new("/edit", "Page list"),
        ];

        return PageLayout.Nav(Label, links, "editor-menu");
    }
}
