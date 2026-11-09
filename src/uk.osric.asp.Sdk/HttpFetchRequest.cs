// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>An outbound HTTP request a page asks the host to make.</summary>
/// <remarks>
/// The host makes the request, not the browser, and only to hosts the operator has allow-listed.
/// Credentials in the URL are refused; a page that needs one puts it in an <c>Authorization</c> or an
/// <c>X-</c> header deliberately, because nothing forwards a credential by accident.
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

    // A blank URL is a mistake in the page and is worth failing on. Everything else about the value -
    // the scheme, the host, credentials, a fragment - is the host's policy, and a fetch that the
    // policy refuses comes back as a value: a page that shows live data branches on the failure
    // rather than catching.
    private static string CheckUrl(string url) {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        return url;
    }
}
