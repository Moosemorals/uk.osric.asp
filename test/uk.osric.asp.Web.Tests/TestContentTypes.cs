// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;

/// <summary>The content-type maps the shell tests render against.</summary>
internal static class TestContentTypes {

    /// <summary>The map shipped in appsettings.json, without reading a settings file.</summary>
    public static PageContentTypes Shipped { get; } = PageContentTypes.Create(
        "text/html; charset=utf-8",
        new Dictionary<string, string?> {
            [".js"] = "text/javascript; charset=utf-8",
            [".mjs"] = "text/javascript; charset=utf-8",
            [".css"] = "text/css; charset=utf-8",
            [".json"] = "application/json; charset=utf-8",
            [".txt"] = "text/plain; charset=utf-8",
        });

    /// <summary>A map with no rows: every key takes the HTML fallback.</summary>
    public static PageContentTypes HtmlOnly { get; } = PageContentTypes.Create("text/html; charset=utf-8", []);
}
