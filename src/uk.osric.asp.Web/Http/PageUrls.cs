// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Http;

using uk.osric.asp.Core.Pages;

/// <summary>Builds the URLs the shell links to, escaped for a URL rather than for HTML.</summary>
/// <remarks>
/// Every method returns a URL ready to be encoded once, by the HTML layer, at the attribute it is
/// written into. Nothing here is HTML-encoded, and nothing returned is HTML-encoded.
/// </remarks>
internal static class PageUrls {

    /// <summary>The public URL of the page at <paramref name="path"/>.</summary>
    public static string Page(PagePath path) =>
        string.Join('/', path.Key.Split('/').Select(Uri.EscapeDataString));

    /// <summary>The editor URL for the page at <paramref name="path"/>.</summary>
    public static string Editor(PagePath path) => "/edit" + Page(path);

    /// <summary>The editor URL for a named command on the page at <paramref name="path"/>.</summary>
    public static string EditorCommand(PagePath path, string command) =>
        Editor(path) + "?cmd=" + Uri.EscapeDataString(command);

    /// <summary>The editor URL that installs a bundled seed over the page at <paramref name="path"/>.</summary>
    public static string EditorInstall(PagePath path) => EditorCommand(path, "install");

    /// <summary>The editor URL that restores one saved revision of the page at <paramref name="path"/>.</summary>
    public static string EditorRestore(PagePath path, string hash) =>
        EditorCommand(path, "restore") + "&hash=" + Uri.EscapeDataString(hash);

    /// <summary>The editor URL for one saved revision of the page at <paramref name="path"/>.</summary>
    public static string EditorRevision(PagePath path, string hash) =>
        EditorCommand(path, "revision") + "&hash=" + Uri.EscapeDataString(hash);

    /// <summary>The editor URL comparing two saved revisions of the page at <paramref name="path"/>.</summary>
    public static string EditorDiff(PagePath path, string from, string to) =>
        EditorCommand(path, "diff") + "&from=" + Uri.EscapeDataString(from) + "&to=" + Uri.EscapeDataString(to);
}
