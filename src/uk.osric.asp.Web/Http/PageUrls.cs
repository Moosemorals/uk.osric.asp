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

    /// <summary>The editor page's URL for the page at <paramref name="path"/>.</summary>
    /// <param name="path">The page to edit.</param>
    /// <param name="editorPath">The editor's own key, which is configuration rather than a constant.</param>
    public static string Editor(PagePath path, string editorPath) => editorPath + "?edit=" + Page(path);

    /// <summary>The editor page's URL for a named command on the page at <paramref name="path"/>.</summary>
    /// <param name="path">The page to edit.</param>
    /// <param name="command">The command the editor page should run.</param>
    /// <param name="editorPath">The editor's own key.</param>
    public static string EditorCommand(PagePath path, string command, string editorPath) =>
        Editor(path, editorPath) + "&cmd=" + Uri.EscapeDataString(command);

    /// <summary>The editor page's URL that installs a bundled seed over the page at <paramref name="path"/>.</summary>
    /// <param name="path">The page to install over.</param>
    /// <param name="editorPath">The editor's own key.</param>
    public static string EditorInstall(PagePath path, string editorPath) =>
        EditorCommand(path, "install", editorPath);

    /// <summary>The editor page's URL that restores one saved revision of the page at <paramref name="path"/>.</summary>
    /// <param name="path">The page to restore.</param>
    /// <param name="hash">The revision to restore.</param>
    /// <param name="editorPath">The editor's own key.</param>
    public static string EditorRestore(PagePath path, string hash, string editorPath) =>
        EditorCommand(path, "restore", editorPath) + "&hash=" + Uri.EscapeDataString(hash);

    /// <summary>The editor page's URL for one saved revision of the page at <paramref name="path"/>.</summary>
    /// <param name="path">The page to read.</param>
    /// <param name="hash">The revision to show.</param>
    /// <param name="editorPath">The editor's own key.</param>
    public static string EditorRevision(PagePath path, string hash, string editorPath) =>
        EditorCommand(path, "revision", editorPath) + "&hash=" + Uri.EscapeDataString(hash);

    /// <summary>The editor page's URL comparing two saved revisions of the page at <paramref name="path"/>.</summary>
    /// <param name="path">The page to compare.</param>
    /// <param name="from">The older revision.</param>
    /// <param name="to">The newer revision.</param>
    /// <param name="editorPath">The editor's own key.</param>
    public static string EditorDiff(PagePath path, string from, string to, string editorPath) =>
        EditorCommand(path, "diff", editorPath) + "&from=" + Uri.EscapeDataString(from) + "&to=" + Uri.EscapeDataString(to);

    /// <summary>The scrub route for the page at <paramref name="path"/>, which is the shell's.</summary>
    public static string Scrub(PagePath path) => "/admin/scrub" + Page(path);
}
