// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Http;

using uk.osric.asp.Core.Pages;

/// <summary>Answers a URL that names no page with a plain-text 404.</summary>
internal static class NotFound {

    // One message for every "this URL names no page" answer - missing, reserved or impossible - so a
    // reader sees the key the shell actually looked for, which is the fastest way to spot a link that
    // encoded a delimiter it meant as a query.
    public static Task WriteAsync(HttpContext context, PagePath path) {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync($"{path} not found.", context.RequestAborted);
    }
}
