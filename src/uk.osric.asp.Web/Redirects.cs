// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

public static class Redirects {

    // 308 rather than 301: both are permanent, but 308 keeps the method, so canonicalising a URL can
    // never turn a POST into a GET and silently drop a save. PathString escaping keeps non-ASCII keys,
    // which normalisation allows, out of a header that Kestrel would reject.
    public static void Permanent(HttpContext context, string url) {
        context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
        context.Response.Headers.Location = new PathString(url).ToUriComponent();
    }
}
