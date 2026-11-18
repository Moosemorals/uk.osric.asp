// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Shell;

/// <summary>The navigation landmark the administration views share.</summary>
internal static class AdminNavigation {

    /// <summary>The label no other navigation landmark may repeat.</summary>
    public const string Label = "Site administration";

    /// <summary>Renders the administration tools the shell offers.</summary>
    /// <param name="context">The request being answered, whose path marks the current tool.</param>
    public static string Nav(HttpContext context) {
        ArgumentNullException.ThrowIfNull(context);

        string path = context.Request.Path.Value ?? "/";
        string editorPath = EditorPaths.Of(context.RequestServices);

        List<NavLink> links = [];
        foreach (ShellLink link in ShellCatalogue.Links(path, string.Empty, editorPath)) {
            // The administration views are where the owner arrived, so the whole section is drawn;
            // the index is the one reader that hides what this visitor cannot reach.
            if (link.Section == ShellSection.Administration) {
                links.Add(new(link.Href, link.Text, ShellCatalogue.IsCurrent(link, path)));
            }
        }

        return ShellDocument.Nav(Label, links, "editor-menu");
    }
}
