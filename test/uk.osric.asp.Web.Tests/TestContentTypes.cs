// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;

/// <summary>The content-type maps the shell tests render against.</summary>
internal static class TestContentTypes {

    private static readonly PageContentTypeRule s_html = new("text/html; charset=utf-8");

    /// <summary>The map shipped in appsettings.json, without reading a settings file.</summary>
    public static PageContentTypes Shipped { get; } = PageContentTypes.Create(
        s_html,
        new Dictionary<string, PageContentTypeRule> {
            [".html"] = s_html,
            [".js"] = new("text/javascript; charset=utf-8"),
            [".mjs"] = new("text/javascript; charset=utf-8"),
            [".css"] = new("text/css; charset=utf-8"),
            [".json"] = new("application/json; charset=utf-8"),
            [".svg"] = new("image/svg+xml; charset=utf-8"),
            [".txt"] = new("text/plain; charset=utf-8"),
        });

    /// <summary>A map with no rows: every key takes the HTML fallback.</summary>
    public static PageContentTypes HtmlOnly { get; } = PageContentTypes.Create(s_html, []);
}
