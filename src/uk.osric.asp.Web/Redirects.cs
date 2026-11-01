// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

internal static class Redirects {

    // 308 rather than 301: both are permanent, but 308 keeps the method, so canonicalising a URL can
    // never turn a POST into a GET and silently drop a save. PathString escaping keeps non-ASCII keys,
    // which normalisation allows, out of a header that Kestrel would reject.
    public static void Permanent(HttpContext context, string path, QueryString query = default) {
        context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
        context.Response.Headers.Location = Join(path, query, default);
    }

    /// <summary>Sends a POST result to a GET of <paramref name="target"/>.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="target">The relative target, which may carry a query and a fragment.</param>
    public static void SeeOther(HttpContext context, string target) {
        context.Response.StatusCode = StatusCodes.Status303SeeOther;
        context.Response.Headers.Location = Encode(target);
    }

    /// <summary>Sends <paramref name="target"/> as the location of a redirect the caller chose.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="status">The status code, normally the page's own.</param>
    /// <param name="target">A relative target, which may carry a query and a fragment, or an absolute URL.</param>
    public static void To(HttpContext context, int status, string target) {
        context.Response.StatusCode = status;
        context.Response.Headers.Location = Encode(target);
    }

    // A page, or the login form's return URL, hands over one string rather than a path and a query, so
    // it is split before either part is encoded. The '#' is found first: QueryString would escape one
    // into '%23', which no browser treats as a fragment.
    private static string Encode(string target) {
        // An absolute URL is already a valid header value and must not be treated as a path.
        if (!target.StartsWith('/')) {
            return target;
        }

        int hash = target.IndexOf('#', StringComparison.Ordinal);
        string beforeHash = hash < 0 ? target : target[..hash];
        int question = beforeHash.IndexOf('?', StringComparison.Ordinal);

        return Join(
            question < 0 ? beforeHash : beforeHash[..question],
            question < 0 ? default : new QueryString(beforeHash[question..]),
            hash < 0 ? default : new FragmentString(target[hash..]));
    }

    // Each part is encoded by the type that understands it. PathString escapes a raw path, and would
    // escape the '?' of a query into '%3F' - a 404 rather than a redirect with parameters - so it is
    // never given the whole URL. A query and a fragment arrive already encoded and pass through.
    private static string Join(string path, QueryString query, FragmentString fragment) =>
        new PathString(path).ToUriComponent() + query.ToUriComponent() + fragment.ToUriComponent();
}
