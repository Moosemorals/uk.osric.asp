// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

using System.Net;

/// <summary>Writes the document a page or a shell view is served in, piece by piece.</summary>
/// <remarks>
/// <para>
/// The builder owns the document's policy, so a seeded page and a shell view cannot disagree about
/// the language, the charset, the title, the sheet order, the icon, the skip link, the landmark or
/// the close. A caller that wants chrome between the body's opening and <c>main</c> writes
/// <see cref="Banner"/> before <see cref="OpenMain"/>, which is what makes the skip link possible.
/// </para>
/// <para>
/// It owns no <em>response</em> policy: status, content type, security headers, caching and the body
/// cap stay with the shell. A builder that could weaken a header would undo more than it fixed.
/// </para>
/// <para>
/// Every member returns markup rather than writing it, so a page writes it with
/// <c>&lt;%== … %&gt;</c> and the shell writes it to its own response. Encoding happens here: the
/// title and every attribute are escaped, so a caller passes raw values.
/// </para>
/// </remarks>
public static class PageDocument {

    /// <summary>The text of the link that skips the chrome.</summary>
    public const string SkipLinkText = "Skip to content";

    /// <summary>The sheets every document links before its own, in this order.</summary>
    /// <remarks><c>/site.css</c> carries the document's shape and the site's look, so a page sheet
    /// overrides a structural rule without a more specific selector.</remarks>
    public static IReadOnlyList<string> BaseStylesheets { get; } = ["/site.css"];

    /// <summary>Whether a value is a path on this site rather than an absolute or protocol-relative URL.</summary>
    /// <param name="href">The value to test.</param>
    /// <remarks>
    /// A sheet or a redirect has to stay on this origin: the site's own policy answers with
    /// <c>style-src 'self'</c>, so an off-site sheet would be blocked silently rather than fail.
    /// </remarks>
    public static bool IsSiteRelative(string? href) {
        if (string.IsNullOrEmpty(href) || href[0] != '/') {
            return false;
        }

        // A second slash or a backslash lets a browser interpret the value as an authority.
        if (href.Length > 1 && (href[1] == '/' || href[1] == '\\')) {
            return false;
        }

        foreach (char character in href) {
            if (character == '\\' || char.IsControl(character)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>Opens the document: the doctype, the head and the open <c>body</c>.</summary>
    /// <param name="title">The document title, encoded here; it may not be blank.</param>
    /// <param name="stylesheets">
    /// The view's own sheets, in the order it asked for them, linked after
    /// <see cref="BaseStylesheets"/>. An exact duplicate is linked once.
    /// </param>
    /// <param name="head">Raw markup written into <c>head</c> last, for a document that describes itself.</param>
    /// <param name="bodyClass">A class for <c>body</c>, when the view styles the document box.</param>
    /// <exception cref="ArgumentException">The title is blank or a sheet is not site-relative.</exception>
    public static string Head(
        string title,
        IReadOnlyList<string>? stylesheets = null,
        string? head = null,
        string? bodyClass = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        IReadOnlyList<string> requested = stylesheets ?? [];
        List<string> sheets = [.. Deduplicated([.. BaseStylesheets, .. requested])];

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>{WebUtility.HtmlEncode(title)}</title>
            {string.Join('\n', sheets.Select(Sheet))}
            <link rel="icon" href="/rocket.svg" type="image/svg+xml">
            {head}
            </head>
            <body{(bodyClass is null ? "" : $" class=\"{WebUtility.HtmlEncode(bodyClass)}\"")}>
            """;
    }

    /// <summary>Writes the skip link and the banner around a navigation landmark.</summary>
    /// <param name="navigation">The nav markup, normally from <see cref="Nav"/>.</param>
    /// <remarks>
    /// The skip link is the first focusable thing in the body and points at the one <c>main</c>, so it
    /// is a property of the document rather than a habit of each caller.
    /// </remarks>
    public static string Banner(string navigation) {
        ArgumentNullException.ThrowIfNull(navigation);

        return $"""
            <a class="skip-link" href="#main">{WebUtility.HtmlEncode(SkipLinkText)}</a>
            <header class="site-header">
            {navigation}
            </header>
            """;
    }

    /// <summary>Renders a navigation landmark from its label and links.</summary>
    /// <param name="label">The landmark's accessible name, which no other <c>nav</c> may repeat.</param>
    /// <param name="links">The links, in reading order.</param>
    /// <param name="className">A class for the <c>nav</c>, when the view styles it.</param>
    /// <exception cref="ArgumentException">The label is blank.</exception>
    /// <remarks>The label is required, so an unnamed navigation landmark cannot be written.</remarks>
    public static string Nav(string label, IReadOnlyList<NavLink> links, string? className = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(links);

        string attribute = className is null ? string.Empty : $" class=\"{WebUtility.HtmlEncode(className)}\"";
        string body = string.Join(" ", links.Select(link => link.IsCurrent
            ? $"<span class=\"current\" aria-current=\"true\">{WebUtility.HtmlEncode(link.Text)}</span>"
            : $"<a href=\"{WebUtility.HtmlEncode(link.Href)}\">{WebUtility.HtmlEncode(link.Text)}</a>"));

        return $"<nav{attribute} aria-label=\"{WebUtility.HtmlEncode(label)}\">{body}</nav>";
    }

    /// <summary>Opens the one <c>main</c> landmark, which the skip link targets.</summary>
    public static string OpenMain() => "<main id=\"main\">";

    /// <summary>Closes the landmark <see cref="OpenMain"/> opened.</summary>
    public static string CloseMain() => "</main>";

    /// <summary>Closes the document.</summary>
    public static string End() => """
        </body>
        </html>
        """;

    // Order is the cascade, so a page is the last word on how it looks: an exact duplicate is
    // linked once, and anything that is not on this site is refused rather than blocked silently.
    private static IEnumerable<string> Deduplicated(IReadOnlyList<string> stylesheets) {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (string href in stylesheets) {
            if (!IsSiteRelative(href)) {
                throw new ArgumentException($"'{href}' is not a site-relative URL.", "stylesheets");
            }

            if (seen.Add(href)) {
                yield return href;
            }
        }
    }

    private static string Sheet(string href) => $"<link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(href)}\">";
}
