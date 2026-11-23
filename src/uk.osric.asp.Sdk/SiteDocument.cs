// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Which of the site's own files a <see cref="SiteDocument"/> is.</summary>
public enum SiteDocumentKind {
    /// <summary>The site's own licence, <c>LICENSE</c>.</summary>
    SiteLicence,

    /// <summary>The notices the image carries for the packages it ships.</summary>
    ThirdPartyNotices,

    /// <summary>One text from the <c>LICENSES</c> folder.</summary>
    LicenceText,

    /// <summary>The REUSE annotation file, <c>REUSE.toml</c>.</summary>
    ReuseAnnotation,
}

/// <summary>How a <see cref="SiteDocument"/>'s text is written.</summary>
public enum SiteDocumentFormat {
    /// <summary>Prose or configuration, to be rendered as it is.</summary>
    Text,

    /// <summary>Markdown, when the page wants to render it.</summary>
    Markdown,
}

/// <summary>One file the deployed image carries.</summary>
/// <param name="Kind">Which of the site's own files it is.</param>
/// <param name="Name">
/// Its name without its folder: <c>LICENSE</c>, <c>THIRD-PARTY-NOTICES.md</c>, <c>REUSE.toml</c> or a
/// licence's short name such as <c>MIT</c>.
/// </param>
/// <param name="Format">How the text is written.</param>
/// <param name="Text">The file exactly as it was read.</param>
/// <remarks>
/// A document says what a file is, never what it looks like: there is no heading and no markup, so a
/// page decides how to present it.
/// </remarks>
public sealed record SiteDocument(
    SiteDocumentKind Kind,
    string Name,
    SiteDocumentFormat Format,
    string Text);
