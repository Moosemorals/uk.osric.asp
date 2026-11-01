// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

internal static class Redirects {

    // 308 rather than 301: both are permanent, but 308 keeps the method, so canonicalising a URL can
    // never turn a POST into a GET and silently drop a save. PathString escaping keeps non-ASCII keys,
    // which normalisation allows, out of a header that Kestrel would reject.
    //
    // The query travels separately: PathString would escape a '?' appended to the path into '%3F', so
    // the two components are encoded by the type that understands each of them.
    public static void Permanent(HttpContext context, string path, QueryString query = default) {
        context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
        context.Response.Headers.Location = new PathString(path).ToUriComponent() + query.ToUriComponent();
    }

    /// <summary>Sends a POST result to a GET of <paramref name="url"/>.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="url">The relative target.</param>
    public static void SeeOther(HttpContext context, string url) {
        context.Response.StatusCode = StatusCodes.Status303SeeOther;
        context.Response.Headers.Location = new PathString(url).ToUriComponent();
    }
}
