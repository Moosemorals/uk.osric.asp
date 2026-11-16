// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Html;

using Microsoft.AspNetCore.WebUtilities;

/// <summary>The one list of links the shell's own routes offer.</summary>
/// <remarks>
/// <para>
/// A link is a value — where it goes, what it says, which menu it belongs to and who it is for — and
/// the renders decide what to do with it. The site menu filters the <see cref="ShellSection.Site"/>
/// section by the visitor and marks the current item; the administration menu takes
/// <see cref="ShellSection.Administration"/> whole, because its own views are the page an owner
/// reached; the administration index filters by audience and writes the rest as a list.
/// </para>
/// <para>
/// It lives in <c>Web</c> because only the shell reads it today. A page that wants these links reads
/// them through the page catalog when IDEA-030 lands, and the values move to <c>Sdk</c> then;
/// nothing here is a page's API.
/// </para>
/// </remarks>
internal static class ShellLinks {

    /// <summary>Every link, in reading order.</summary>
    /// <param name="path">The request's path, which the sign-in link returns to.</param>
    /// <param name="query">The request's raw query, carried into the sign-in link.</param>
    /// <param name="editorPath">The editor's own key, which is configuration.</param>
    /// <remarks>
    /// The list is the same for every visitor; <see cref="IsFor"/> is how a renderer narrows it.
    /// </remarks>
    public static IReadOnlyList<ShellLink> All(string path, string query, string editorPath) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(editorPath);

        return [
            new("/", "Home", ShellSection.Site, ShellAudience.Everyone, ShellMatch.Leaf),
            new(SignIn(path, query), "Sign in", ShellSection.Site, ShellAudience.Anonymous, ShellMatch.Leaf),
            new("/assistant", "Assistant", ShellSection.Site, ShellAudience.Owner, ShellMatch.Subtree),
            new(editorPath, "Pages", ShellSection.Site, ShellAudience.Owner, ShellMatch.Subtree),
            new("/admin", "Admin", ShellSection.Site, ShellAudience.Owner, ShellMatch.Subtree),
            new("/logout", "Sign out", ShellSection.Site, ShellAudience.Owner, ShellMatch.Leaf),

            new("/admin", "Administration", ShellSection.Administration, ShellAudience.Everyone, ShellMatch.Subtree),
            new("/admin/import", "Import pages", ShellSection.Administration, ShellAudience.Owner, ShellMatch.Subtree),
            new("/admin/export", "Export pages", ShellSection.Administration, ShellAudience.Owner, ShellMatch.Subtree),
            new("/admin/changes", "Changelist", ShellSection.Administration, ShellAudience.Owner, ShellMatch.Leaf),
            new("/admin/restore", "Restore seeds", ShellSection.Administration, ShellAudience.Owner, ShellMatch.Leaf),
            new("/admin/seed-apply", "Apply seed updates", ShellSection.Administration, ShellAudience.Owner, ShellMatch.Leaf),
            new("/admin/reference", "Reference", ShellSection.Administration, ShellAudience.Everyone, ShellMatch.Subtree),
            new("/admin/copyright", "Copyright and licences", ShellSection.Administration, ShellAudience.Everyone, ShellMatch.Leaf),
            new("/status", "Status", ShellSection.Administration, ShellAudience.Owner, ShellMatch.Subtree),
            new(editorPath, "Page list", ShellSection.Administration, ShellAudience.Owner, ShellMatch.Subtree),
            new("/help", "Help", ShellSection.Administration, ShellAudience.Everyone, ShellMatch.Subtree),
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

    // The editor the visitor lands on after signing in carries the menu, so a page's own return path
    // only has to survive as a query parameter, not as an absolute URL.
    private static string SignIn(string path, string query) =>
        "/login" + QueryString.Create("returnUrl", path + query).ToUriComponent();
}

/// <summary>Which menu a shell link belongs to.</summary>
internal enum ShellSection {
    /// <summary>The site menu every shell document carries.</summary>
    Site,

    /// <summary>The administration menu, and the index it belongs to.</summary>
    Administration,
}

/// <summary>Who a shell link is for.</summary>
internal enum ShellAudience {
    /// <summary>Every visitor, signed in or not.</summary>
    Everyone,

    /// <summary>A visitor who has not signed in.</summary>
    Anonymous,

    /// <summary>A signed-in visitor.</summary>
    Owner,
}

/// <summary>How a request path marks a link current.</summary>
internal enum ShellMatch {
    /// <summary>The path is the link's own, and nothing below it.</summary>
    Leaf,

    /// <summary>The path is the link's own, or a page or route beneath it.</summary>
    Subtree,
}

/// <summary>One link the shell offers: where it goes, what it says, its menu, its audience and how it matches.</summary>
internal readonly record struct ShellLink(
    string Href,
    string Text,
    ShellSection Section,
    ShellAudience Audience,
    ShellMatch Match);
