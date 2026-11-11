// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>An outbound HTTP request a page asks the host to make.</summary>
/// <remarks>
/// The host makes the request, not the browser, to any <c>http</c> or <c>https</c> host the URL
/// names. Credentials in the URL are refused. A page that needs one names an operator-configured
/// credential with <see cref="Credential"/>; the header allow-list still admits <c>Authorization</c>
/// and <c>X-</c> headers for a value the page itself already holds.
/// </remarks>
/// <param name="Url">An absolute <c>http</c> or <c>https</c> URL, with no credentials and no fragment.</param>
public sealed record HttpFetchRequest(string Url) {

    /// <summary>An absolute <c>http</c> or <c>https</c> URL, with no credentials and no fragment.</summary>
    /// <exception cref="ArgumentException">The URL is blank.</exception>
    public string Url { get; init; } = CheckUrl(Url);

    /// <summary>The method; <see cref="HttpFetchMethod.Get"/> by default.</summary>
    public HttpFetchMethod Method { get; init; } = HttpFetchMethod.Get;

    /// <summary>The request body. <c>POST</c> only, and text only.</summary>
    public string? Body { get; init; }

    /// <summary>
    /// Additional request headers. The host filters and validates them: a small allow-list of names
    /// plus any <c>X-</c> name, and no line breaks.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();

    /// <summary>The name of an operator-configured credential the host attaches to this request.</summary>
    /// <remarks>
    /// <para>A name, never a value: the page never sees the value.</para>
    /// <para>
    /// The host sends the credential only to the host it is configured for, and only from the page
    /// it is bound to. It drops the header on a redirect to another host or to plaintext. A page that
    /// does not know a credential name asks the operator rather than inventing one; an unknown name
    /// is an <see cref="HttpFetchFailure.InvalidRequest"/> and no request is made.
    /// </para>
    /// <para><see langword="null"/>, the default, means no credential.</para>
    /// </remarks>
    public string? Credential { get; init; }

    // A blank URL is a mistake in the page and is worth failing on. Everything else about the value -
    // the scheme, the host, credentials, a fragment - is the host's policy, and a fetch that the
    // policy refuses comes back as a value: a page that shows live data branches on the failure
    // rather than catching.
    private static string CheckUrl(string url) {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        return url;
    }
}
