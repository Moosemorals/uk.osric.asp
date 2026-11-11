// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Net;
using System.Text;
using uk.osric.asp.Sdk;

/// <summary>Makes a bounded, text-only outbound HTTP request.</summary>
/// <remarks>
/// <para>
/// The mechanics behind both <see cref="IPageFetcher"/> and the assistant's fetch tool. Every refusal
/// is a value rather than an exception, so a caller can fall back without a <c>try</c>. Nothing here
/// logs a query, a header or a body: the host is named, and the caller's own log lines are the place
/// for anything more specific.
/// </para>
/// <para>
/// There is no allow-list: any absolute <c>http</c> or <c>https</c> URL is reachable, and the caller
/// decides whether that is acceptable for its audience (DEC-216). The remaining guards are about
/// shape rather than destination — no credentials in the URL, no fragment, no cookies, no automatic
/// redirects, and a redirect hop is held to the same scheme rule before any connection is made.
/// </para>
/// </remarks>
internal sealed class OutboundFetcher(
    IHttpClientFactory clients,
    PageFetchOptions options,
    ILogger<OutboundFetcher> logger) {

    /// <summary>The named client whose handler the host pools.</summary>
    public const string ClientName = "outbound-fetch";

    /// <summary>Performs one request, bounded by <paramref name="aborted"/> and <paramref name="deadline"/>.</summary>
    /// <param name="request">The request to make.</param>
    /// <param name="caller">Who asked, for the log line: a page key or a tool name.</param>
    /// <param name="origin">Whether page code or the assistant asked, for the <c>User-Agent</c>.</param>
    /// <param name="aborted">Cancelled when the caller gives up.</param>
    /// <param name="deadline">Cancelled when the caller's budget expires.</param>
    public async Task<HttpFetchResponse> FetchAsync(
        HttpFetchRequest request,
        string caller,
        OutboundOrigin origin,
        CancellationToken aborted,
        CancellationToken deadline) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);

        return await SendAsync(request, caller, origin, aborted, deadline);
    }

    private async Task<HttpFetchResponse> SendAsync(
        HttpFetchRequest request,
        string caller,
        OutboundOrigin origin,
        CancellationToken aborted,
        CancellationToken deadline) {
        if (!TryCreateUri(request.Url, out Uri? current) || !TryValidate(request, out string? mediaType)) {
            return Refuse(HttpFetchFailure.InvalidRequest, caller, request.Url, LogLevel.Warning);
        }

        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(aborted, deadline);
        budget.CancelAfter(options.Timeout);

        HttpMethod method = request.Method == HttpFetchMethod.Post ? HttpMethod.Post : HttpMethod.Get;
        string? body = method == HttpMethod.Post ? request.Body : null;

        for (int redirects = 0; ; redirects++) {
            using HttpRequestMessage message = new(method, current);
            if (body is not null) {
                message.Content = new StringContent(body, Encoding.UTF8, mediaType ?? "text/plain");
            }

            foreach ((string name, string value) in request.Headers) {
                // Content-Type belongs on the content, and was applied to it above; User-Agent is
                // the host's, so a page cannot claim to be a different client.
                if (!string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase)) {
                    message.Headers.TryAddWithoutValidation(name, value);
                }
            }
            message.Headers.TryAddWithoutValidation("User-Agent", OutboundUserAgent.For(origin));

            HttpResponseMessage response;
            try {
                response = await SendAsync(message, budget.Token);
            } catch (OperationCanceledException) {
                return aborted.IsCancellationRequested
                    ? Refuse(HttpFetchFailure.Aborted, caller, request.Url, LogLevel.Information)
                    : Refuse(HttpFetchFailure.Timeout, caller, request.Url, LogLevel.Information);
            } catch (HttpRequestException failure) {
                logger.LogInformation(failure, "Fetch for {Caller} could not reach {Host}", caller, current.IdnHost);

                return Empty(HttpFetchFailure.Network);
            }

            using (response) {
                if (!IsRedirect(response.StatusCode)) {
                    return await ReadAsync(response, HttpFetchFailure.None, budget.Token);
                }

                Uri? location = response.Headers.Location is { } target
                    ? target.IsAbsoluteUri ? target : new Uri(current, target)
                    : null;

                if (location is null || options.MaxRedirects == 0) {
                    return await ReadAsync(response, HttpFetchFailure.None, budget.Token);
                }

                if (redirects >= options.MaxRedirects) {
                    return await ReadAsync(response, HttpFetchFailure.TooManyRedirects, budget.Token);
                }

                if (!IsFetchable(location)) {
                    // The caller still sees where it pointed and the status it answered with, but no
                    // connection is made to the target it named.
                    return await ReadAsync(response, HttpFetchFailure.InvalidRequest, budget.Token);
                }

                // The browser convention: a redirected POST becomes a bodiless GET unless the status
                // says the method and body are preserved.
                if (response.StatusCode is HttpStatusCode.MovedPermanently
                    or HttpStatusCode.Found
                    or HttpStatusCode.SeeOther) {
                    method = HttpMethod.Get;
                    body = null;
                }

                current = location;
            }
        }
    }

    // The client is cheap and owns no socket; the handler behind it is pooled by the factory, so a
    // per-call client is what keeps the singleton fetcher from capturing one for the process's life.
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken) {
        using HttpClient client = clients.CreateClient(ClientName);

        return await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static bool TryCreateUri(string url, out Uri uri) {
        uri = null!;

        return Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
            && parsed is not null
            && IsFetchable(parsed)
            && (uri = parsed) is not null;
    }

    // A redirect target is held to the same rule as the original request.
    private static bool IsFetchable(Uri uri) {
        return (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            && uri.UserInfo.Length == 0
            && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool TryValidate(HttpFetchRequest request, out string? mediaType) {
        mediaType = null;

        if (request.Method is not (HttpFetchMethod.Get or HttpFetchMethod.Post)) {
            return false;
        }

        if (request.Method == HttpFetchMethod.Get && request.Body is not null) {
            return false;
        }

        foreach ((string name, string value) in request.Headers) {
            if (!PageFetchHeaders.IsRequestAllowed(name)
                || value.Contains('\r')
                || value.Contains('\n')) {
                return false;
            }

            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)) {
                mediaType = value;
            }
        }

        return true;
    }

    private static bool IsRedirect(HttpStatusCode status) {
        return status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private async Task<HttpFetchResponse> ReadAsync(
        HttpResponseMessage response,
        HttpFetchFailure failure,
        CancellationToken cancellationToken) {
        byte[] body = await ReadBodyAsync(response, cancellationToken);
        bool truncated = body.Length > options.MaxResponseBytes;
        string text = Decode(response, truncated ? body[..options.MaxResponseBytes] : body);

#pragma warning disable IDE0028 // A collection expression would discard the case-insensitive comparer.
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers) {
            if (PageFetchHeaders.IsResponseAllowed(header.Key)) {
                headers[header.Key] = string.Join(", ", header.Value);
            }
        }

        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers) {
            if (PageFetchHeaders.IsResponseAllowed(header.Key)) {
                headers[header.Key] = string.Join(", ", header.Value);
            }
        }

        HttpFetchFailure outcome = failure == HttpFetchFailure.None && (int)response.StatusCode >= 400
            ? HttpFetchFailure.HttpError
            : failure;

        return new HttpFetchResponse((int)response.StatusCode, headers, text, outcome, truncated);
    }

    // Content-Length is not trusted to be small, so the cap is applied to what arrives.
    private async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[8192];
        while (true) {
            int read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) {
                break;
            }

            buffer.Write(chunk, 0, read);
            if (buffer.Length > options.MaxResponseBytes) {
                break;
            }
        }

        return buffer.ToArray();
    }

    private string Decode(HttpResponseMessage response, byte[] body) {
        string? charset = response.Content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrWhiteSpace(charset)) {
            try {
                return Encoding.GetEncoding(charset.Trim('"')).GetString(body);
            } catch (ArgumentException exception) {
                // An unknown charset is the caller's problem to spot; UTF-8 is the least surprising guess.
                logger.LogDebug(exception, "A fetched response named the unknown charset {Charset}; it was read as UTF-8", charset);
            }
        }

        return Encoding.UTF8.GetString(body);
    }

    private HttpFetchResponse Refuse(HttpFetchFailure failure, string caller, string url, LogLevel level) {
        // The host, not the URL: a query can carry a secret, and the host name is enough to explain
        // the refusal.
        string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? parsed.IdnHost : "(unparsed)";
        logger.Log(level, "Fetch for {Caller} was refused a request to {Host}: {Failure}", caller, host, failure);

        return Empty(failure);
    }

    private static HttpFetchResponse Empty(HttpFetchFailure failure) {
        return new HttpFetchResponse(0, new Dictionary<string, string>(), string.Empty, failure, false);
    }
}
