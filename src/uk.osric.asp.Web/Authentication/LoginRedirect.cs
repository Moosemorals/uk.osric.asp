// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using Microsoft.AspNetCore.Http;
using uk.osric.asp.Core.Pages;

/// <summary>Builds the sign-in URL a page redirects an anonymous visitor to.</summary>
/// <remarks>
/// The target is built from the page's canonical key and the request's <em>raw</em> query, never from
/// the page's collapsed query dictionary: a URL like <c>/search?q=a&amp;q=b</c> must return to both
/// values. <see cref="LocalRedirect"/> is still the only thing that turns a <c>returnUrl</c> back into
/// a location, so this helper cannot become an open redirect.
/// </remarks>
internal static class LoginRedirect {

    /// <summary>The <c>/login</c> URL that returns to <paramref name="path"/> with its query.</summary>
    /// <param name="path">The page's canonical key.</param>
    /// <param name="query">The request's query string, as it arrived.</param>
    public static string For(PagePath path, QueryString query) {
        ArgumentNullException.ThrowIfNull(path);

        string returnUrl = path.Key + query.ToUriComponent();

        return "/login" + QueryString.Create("returnUrl", returnUrl).ToUriComponent();
    }
}
