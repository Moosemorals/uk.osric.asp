// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>An operator-configured credential a page names but never reads.</summary>
/// <remarks>
/// <para>
/// Only <see cref="HeaderValue"/> reads the secret, and only <see cref="OutboundFetcher"/> calls it as
/// it attaches the header. <see cref="ToString"/> deliberately leaves the value out, so an accidental
/// log line cannot carry it.
/// </para>
/// <para>
/// The credential is bound to one page or to a page and everything under it, so pages that share an
/// API name one entry rather than duplicating the secret for each of them.
/// </para>
/// <para>
/// It is a class rather than a record because the generated record <c>ToString</c> would print every
/// property, the value included.
/// </para>
/// </remarks>
internal sealed class FetchCredential(
    string name,
    string host,
    string header,
    string? scheme,
    string value,
    PagePath page,
    bool includesDescendants) : IOutboundCredential {

    /// <summary>The name a page uses to ask for this credential.</summary>
    public string Name { get; } = name;

    /// <summary>The only host the header may be sent to, in <see cref="Uri.IdnHost"/> form.</summary>
    public string Host { get; } = host;

    /// <summary>The request header that carries the value.</summary>
    public string Header { get; } = header;

    /// <summary>The page this credential is bound to, or the root of the subtree it is bound to.</summary>
    public PagePath Page { get; } = page;

    /// <summary>Whether the pages under <see cref="Page"/> may name this credential too.</summary>
    public bool IncludesDescendants { get; } = includesDescendants;

    /// <summary>The pages that may name this credential, as a phrase safe to log.</summary>
    public string Scope => (Page.Key, IncludesDescendants) switch {
        ("/", true) => "every page",
        (_, true) => $"{Page.Key} and below",
        _ => Page.Key,
    };

    /// <summary>The header value: the scheme and one space, then the secret, or the secret alone.</summary>
    public string HeaderValue => string.IsNullOrEmpty(scheme) ? value : $"{scheme} {value}";

    /// <summary>Whether this credential may be attached to a request for <paramref name="uri"/>.</summary>
    /// <param name="uri">The request or redirect target.</param>
    /// <returns><see langword="true"/> on the credential's host over HTTPS, or over plaintext when that
    /// host is loopback.</returns>
    public bool AppliesTo(Uri uri) {
        ArgumentNullException.ThrowIfNull(uri);

        return string.Equals(uri.IdnHost, Host, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && uri.IsLoopback));
    }

    /// <summary>Whether this credential belongs to the page with <paramref name="key"/>.</summary>
    /// <param name="key">The canonical page key.</param>
    /// <returns>
    /// <see langword="true"/> for the page named in the credential's configuration, and for any page
    /// below it when the entry's <c>Page</c> ended in <c>/*</c>.
    /// </returns>
    public bool IsBoundTo(string key) {
        ArgumentNullException.ThrowIfNull(key);

        if (string.Equals(Page.Key, key, StringComparison.Ordinal)) {
            return true;
        }

        if (!IncludesDescendants) {
            return false;
        }

        // The boundary is the slash, so a scope of /tools does not reach /toolshed.
        string prefix = Page.Key == "/" ? "/" : Page.Key + "/";
        return key.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>Names the credential and its scope, never its value.</summary>
    /// <returns>A line safe to log.</returns>
    public override string ToString() => $"credential {Name} for {Header} on {Host} from {Scope}";
}
