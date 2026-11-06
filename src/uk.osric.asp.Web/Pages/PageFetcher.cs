// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics;
using System.Net;
using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>Makes a page's outbound HTTP request, bounded and allow-listed.</summary>
/// <remarks>
/// <para>
/// Every refusal is a value rather than an exception, so a page can fall back without a <c>try</c>.
/// The allow-list is checked before any socket is opened, and again for every redirect hop, because
/// a permitted host is allowed to point anywhere and that would otherwise be the hole.
/// </para>
/// <para>
/// Nothing here logs a query, a header or a body: the host is named, and the page's own <c>Log</c>
/// calls are the place for anything more specific.
/// </para>
/// </remarks>
internal sealed class PageFetcher(
    IHttpClientFactory clients,
    PageFetchOptions options,
    PageMetrics metrics,
    ILogger<PageFetcher> logger) : IPageFetcher {

    /// <summary>The named client whose handler the host pools.</summary>
    public const string ClientName = "page-fetch";

    /// <inheritdoc/>
    public async Task<PageFetchResponse> FetchAsync(
        PageFetchRequest request,
        string pagePath,
        CancellationToken aborted,
        CancellationToken deadline) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(pagePath);

        long start = Stopwatch.GetTimestamp();
        PageFetchResponse response = await SendAsync(request, pagePath, aborted, deadline);

        metrics.RecordFetch(PagePath.Normalize(pagePath), OutcomeOf(response), Stopwatch.GetElapsedTime(start));

        return response;
    }

    private async Task<PageFetchResponse> SendAsync(
        PageFetchRequest request,
        string pagePath,
        CancellationToken aborted,
        CancellationToken deadline) {
        if (!TryCreateUri(request.Url, out Uri? current) || !TryValidate(request, out string? mediaType)) {
            return Refuse(PageFetchFailure.InvalidRequest, pagePath, request.Url, LogLevel.Warning);
        }

        if (!options.Allows(current.IdnHost)) {
            return Refuse(PageFetchFailure.NotAllowed, pagePath, request.Url, LogLevel.Warning);
        }

        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(aborted, deadline);
        budget.CancelAfter(options.Timeout);

        HttpMethod method = request.Method == PageFetchMethod.Post ? HttpMethod.Post : HttpMethod.Get;
        string? body = method == HttpMethod.Post ? request.Body : null;

        for (int redirects = 0; ; redirects++) {
            using HttpRequestMessage message = new(method, current);
            if (body is not null) {
                message.Content = new StringContent(body, Encoding.UTF8, mediaType ?? "text/plain");
            }

            foreach ((string name, string value) in request.Headers) {
                // Content-Type belongs on the content, and was applied to it above.
                if (!string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)) {
                    message.Headers.TryAddWithoutValidation(name, value);
                }
            }

            HttpResponseMessage response;
            try {
                response = await SendAsync(message, budget.Token);
            } catch (OperationCanceledException) {
                return aborted.IsCancellationRequested
                    ? Refuse(PageFetchFailure.Aborted, pagePath, request.Url, LogLevel.Information)
                    : Refuse(PageFetchFailure.Timeout, pagePath, request.Url, LogLevel.Information);
            } catch (HttpRequestException failure) {
                logger.LogInformation(failure, "Page {Path} could not reach {Host}", pagePath, current.IdnHost);

                return Empty(PageFetchFailure.Network);
            }

            using (response) {
                if (!IsRedirect(response.StatusCode)) {
                    return await ReadAsync(response, PageFetchFailure.None, budget.Token);
                }

                Uri? location = response.Headers.Location is { } target
                    ? target.IsAbsoluteUri ? target : new Uri(current, target)
                    : null;

                if (location is null || options.MaxRedirects == 0) {
                    return await ReadAsync(response, PageFetchFailure.None, budget.Token);
                }

                if (redirects >= options.MaxRedirects) {
                    return await ReadAsync(response, PageFetchFailure.TooManyRedirects, budget.Token);
                }

                if (!IsFetchable(location) || !options.Allows(location.IdnHost)) {
                    // The page still sees where it pointed and the status it answered with, but no
                    // connection is made to the host it named.
                    return await ReadAsync(response, PageFetchFailure.NotAllowed, budget.Token);
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
            && (string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                || string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            && parsed.UserInfo.Length == 0
            && string.IsNullOrEmpty(parsed.Fragment)
            && (uri = parsed) is not null;
    }

    // A redirect target is held to the same rule as the original request.
    private static bool IsFetchable(Uri uri) {
        return (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            && uri.UserInfo.Length == 0
            && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool TryValidate(PageFetchRequest request, out string? mediaType) {
        mediaType = null;

        if (request.Method is not (PageFetchMethod.Get or PageFetchMethod.Post)) {
            return false;
        }

        if (request.Method == PageFetchMethod.Get && request.Body is not null) {
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

    private async Task<PageFetchResponse> ReadAsync(
        HttpResponseMessage response,
        PageFetchFailure failure,
        CancellationToken cancellationToken) {
        byte[] body = await ReadBodyAsync(response, cancellationToken);
        bool truncated = body.Length > options.MaxResponseBytes;
        string text = Decode(response, truncated ? body[..options.MaxResponseBytes] : body);

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
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

        PageFetchFailure outcome = failure == PageFetchFailure.None && (int)response.StatusCode >= 400
            ? PageFetchFailure.HttpError
            : failure;

        return new PageFetchResponse((int)response.StatusCode, headers, text, outcome, truncated);
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

    private static string Decode(HttpResponseMessage response, byte[] body) {
        string? charset = response.Content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrWhiteSpace(charset)) {
            try {
                return Encoding.GetEncoding(charset.Trim('"')).GetString(body);
            } catch (ArgumentException) {
                // An unknown charset is the page's problem to spot; UTF-8 is the least surprising guess.
            }
        }

        return Encoding.UTF8.GetString(body);
    }

    private PageFetchResponse Refuse(PageFetchFailure failure, string pagePath, string url, LogLevel level) {
        // The host, not the URL: a query can carry a secret, and a refusal is usually a missing
        // allow-list entry that the host name is enough to explain.
        string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? parsed.IdnHost : "(unparsed)";
        logger.Log(level, "Page {Path} was refused a fetch to {Host}: {Failure}", pagePath, host, failure);

        return Empty(failure);
    }

    private static PageFetchResponse Empty(PageFetchFailure failure) {
        return new PageFetchResponse(0, new Dictionary<string, string>(), string.Empty, failure, false);
    }

    private static string OutcomeOf(PageFetchResponse response) {
        return response.Failure switch {
            PageFetchFailure.None => PageMetrics.FetchOk,
            PageFetchFailure.HttpError => PageMetrics.FetchHttpError,
            PageFetchFailure.NotAllowed => PageMetrics.FetchNotAllowed,
            PageFetchFailure.Timeout => PageMetrics.FetchTimeout,
            PageFetchFailure.Network => PageMetrics.FetchNetwork,
            PageFetchFailure.InvalidRequest => PageMetrics.FetchInvalid,
            PageFetchFailure.TooManyRequests => PageMetrics.FetchLimit,
            PageFetchFailure.TooManyRedirects => PageMetrics.FetchRedirectLimit,
            _ => PageMetrics.FetchAborted,
        };
    }
}
