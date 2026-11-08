// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Makes bounded outbound HTTP requests.</summary>
/// <remarks>
/// One capability, three call sites: the two conveniences cover the ordinary case and are default
/// interface members over the general form, so a host implements one method and a new HTTP verb
/// stays a value in <see cref="HttpFetchMethod"/> rather than a new member here.
/// </remarks>
public interface IHttpFetch {

    /// <summary>Makes one bounded outbound HTTP request.</summary>
    /// <param name="request">The request to make.</param>
    /// <returns>The response. A failure is a value, not an exception.</returns>
    public Task<HttpFetchResponse> SendAsync(HttpFetchRequest request);

    /// <summary>Makes a <c>GET</c> request.</summary>
    /// <param name="url">An absolute <c>http</c> or <c>https</c> URL.</param>
    /// <returns>The response. A failure is a value, not an exception.</returns>
    public Task<HttpFetchResponse> GetAsync(string url) =>
        SendAsync(new HttpFetchRequest(url));

    /// <summary>Makes a <c>POST</c> request with a text body.</summary>
    /// <param name="url">An absolute <c>http</c> or <c>https</c> URL.</param>
    /// <param name="body">The text body; the host sends it as <c>text/plain</c> unless a header says otherwise.</param>
    /// <returns>The response. A failure is a value, not an exception.</returns>
    public Task<HttpFetchResponse> PostAsync(string url, string body) =>
        SendAsync(new HttpFetchRequest(url) {
            Method = HttpFetchMethod.Post,
            Body = body,
        });
}
