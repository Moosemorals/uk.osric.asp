// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Http;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;

/// <summary>Answers a URL that names no page with a 404 the caller can act on.</summary>
internal static class NotFound {

    // One message for every "this URL names no page" answer - missing, reserved or impossible - so a
    // reader sees the key the shell actually looked for, which is the fastest way to spot a link that
    // encoded a delimiter it meant as a query.
    public static Task WriteAsync(HttpContext context, PagePath path) {
        bool isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        List<NavLink> remedies = [];
        if (isAuthenticated && path.IsAddressable) {
            // The edit form creates the page on its first save, so the most useful next step for an
            // owner who followed a stale link is the editor, already pointed at the key that missed.
            remedies.Add(new(PageUrls.Editor(path), "Create this page"));
        }

        remedies.AddRange(ErrorMessage.DefaultRemedies(isAuthenticated));

        ErrorMessage message = new(
            "page.not_found",
            "Page not found",
            $"{path} not found.",
            remedies);

        return ErrorResponses.WriteAsync(context, StatusCodes.Status404NotFound, message);
    }
}
