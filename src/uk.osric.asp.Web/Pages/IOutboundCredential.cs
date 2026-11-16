// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>A header that may be attached to some of an outbound request's hops.</summary>
/// <remarks>
/// <para>
/// The one thing <see cref="OutboundFetcher"/> asks of a credential: for this hop, is there a header,
/// and what is it? Where a credential may go is the credential's own rule, so the page's rule and the
/// view's are stated once each rather than re-implemented by every caller — and there is exactly one
/// place that puts a header on a message, which is what keeps it off a redirect to another host.
/// </para>
/// <para>
/// <see cref="HeaderValue"/> reads the secret, so only the fetcher calls it. <see cref="Name"/> is what
/// belongs in a log line.
/// </para>
/// </remarks>
internal interface IOutboundCredential {

    /// <summary>A name safe to log, which never contains the value.</summary>
    string Name { get; }

    /// <summary>The request header the value is sent in.</summary>
    string Header { get; }

    /// <summary>The header value: the scheme and one space, then the secret.</summary>
    string HeaderValue { get; }

    /// <summary>Whether this credential may be attached to a request for <paramref name="uri"/>.</summary>
    /// <param name="uri">The request or redirect target.</param>
    bool AppliesTo(Uri uri);
}
