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
/// decides whether that is acceptable for its audience (`pages-outbound-fetch`). The remaining guards are about
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
    /// <remarks>A caller with no credential, which is every assistant request.</remarks>
    public Task<HttpFetchResponse> FetchAsync(
        HttpFetchRequest request,
        string caller,
        OutboundOrigin origin,
        CancellationToken aborted,
        CancellationToken deadline) {
        return FetchAsync(request, caller, origin, credential: null, aborted, deadline);
    }

    /// <summary>Performs one request, attaching <paramref name="credential"/> only where it applies.</summary>
    /// <param name="request">The request to make.</param>
    /// <param name="caller">Who asked, for the log line: a page key or a tool name.</param>
    /// <param name="origin">Whether page code or the assistant asked, for the <c>User-Agent</c>.</param>
    /// <param name="credential">A page-bound credential, or <see langword="null"/> for none.</param>
    /// <param name="aborted">Cancelled when the caller gives up.</param>
    /// <param name="deadline">Cancelled when the caller's budget expires.</param>
    public async Task<HttpFetchResponse> FetchAsync(
        HttpFetchRequest request,
        string caller,
        OutboundOrigin origin,
        PageCredential? credential,
        CancellationToken aborted,
        CancellationToken deadline) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);

        if (!TryPrepareRequest(request, caller, credential, out Uri? current, out string? mediaType)) {
            return Empty(HttpFetchFailure.InvalidRequest);
        }

        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(aborted, deadline);
        budget.CancelAfter(options.Timeout);
        (HttpResponseMessage? response, HttpFetchFailure failure) = await SendUntilResponseAsync(
            request, caller, origin, credential, current, mediaType, aborted, budget.Token);
        if (response is null) {
            return Empty(failure);
        }

        using (response) {
            try {
                return await ReadAsync(response, failure, budget.Token);
            } catch (OperationCanceledException) {
                return Refuse(
                    aborted.IsCancellationRequested ? HttpFetchFailure.Aborted : HttpFetchFailure.Timeout,
                    caller,
                    request.Url,
                    LogLevel.Information);
            } catch (IOException exception) {
                logger.LogInformation(exception, "Fetch for {Caller} lost its response body from {Host}", caller, current.IdnHost);

                return Empty(HttpFetchFailure.Network);
            }
        }
    }

    /// <summary>Opens a bounded text stream for one request.</summary>
    /// <param name="request">The request to make.</param>
    /// <param name="caller">Who asked, for the log line.</param>
    /// <param name="origin">Whether page code or the assistant asked, for the <c>User-Agent</c>.</param>
    /// <param name="credential">A page-bound credential, or <see langword="null"/> for none.</param>
    /// <param name="aborted">Cancelled when the caller gives up.</param>
    /// <param name="deadline">Cancelled when the caller's lifetime ends.</param>
    /// <param name="onClosed">Records the completed stream.</param>
    public async Task<HttpFetchStream> StreamAsync(
        HttpFetchRequest request,
        string caller,
        OutboundOrigin origin,
        PageCredential? credential,
        Action<HttpFetchStream> onClosed,
        CancellationToken aborted,
        CancellationToken deadline) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);
        ArgumentNullException.ThrowIfNull(onClosed);

        if (!TryPrepareRequest(request, caller, credential, out Uri? current, out string? mediaType)) {
            return new EmptyHttpFetchStream(HttpFetchFailure.InvalidRequest, onClosed);
        }

        // The header budget ends at the response headers; body reads have independent clocks.
        using CancellationTokenSource headerBudget = CancellationTokenSource.CreateLinkedTokenSource(aborted, deadline);
        headerBudget.CancelAfter(options.Timeout);
        (HttpResponseMessage? response, HttpFetchFailure failure) = await SendUntilResponseAsync(
            request, caller, origin, credential, current, mediaType, aborted, headerBudget.Token);
        if (response is null) {
            return new EmptyHttpFetchStream(failure, onClosed);
        }

        HttpFetchFailure outcome = failure == HttpFetchFailure.None && (int)response.StatusCode >= 400
            ? HttpFetchFailure.HttpError
            : failure;

        try {
            Stream body = await response.Content.ReadAsStreamAsync(headerBudget.Token);
            return new OutboundHttpFetchStream(
                response,
                body,
                ReadHeaders(response),
                outcome,
                EncodingFor(response),
                options.StreamSilence,
                options.StreamMax,
                options.MaxStreamBytes,
                aborted,
                deadline,
                onClosed);
        } catch (OperationCanceledException) {
            response.Dispose();

            HttpFetchFailure cancelled = aborted.IsCancellationRequested
                ? HttpFetchFailure.Aborted
                : HttpFetchFailure.Timeout;
            _ = Refuse(cancelled, caller, request.Url, LogLevel.Information);

            return new EmptyHttpFetchStream(cancelled, onClosed);
        } catch (HttpRequestException exception) {
            response.Dispose();
            logger.LogInformation(exception, "Fetch for {Caller} could not open the response body from {Host}", caller, current.IdnHost);

            return new EmptyHttpFetchStream(HttpFetchFailure.Network, onClosed);
        } catch (IOException exception) {
            response.Dispose();
            logger.LogInformation(exception, "Fetch for {Caller} could not open the response body from {Host}", caller, current.IdnHost);

            return new EmptyHttpFetchStream(HttpFetchFailure.Network, onClosed);
        }
    }

    private bool TryPrepareRequest(
        HttpFetchRequest request,
        string caller,
        PageCredential? credential,
        out Uri current,
        out string? mediaType) {
        mediaType = null;
        if (!TryCreateUri(request.Url, out Uri? parsed) || !TryValidate(request, out mediaType)) {
            _ = Refuse(HttpFetchFailure.InvalidRequest, caller, request.Url, LogLevel.Warning);
            current = null!;
            return false;
        }

        current = parsed;
        if (credential is null || credential.AppliesTo(current)) {
            return true;
        }

        logger.LogWarning(
            "Fetch for {Caller} was refused credential {Credential} for {Host}: it is not the credential's host or is not secure",
            caller,
            credential.Name,
            current.IdnHost);

        return false;
    }

    private async Task<(HttpResponseMessage? Response, HttpFetchFailure Failure)> SendUntilResponseAsync(
        HttpFetchRequest request,
        string caller,
        OutboundOrigin origin,
        PageCredential? credential,
        Uri current,
        string? mediaType,
        CancellationToken aborted,
        CancellationToken cancellationToken) {
        HttpMethod method = request.Method == HttpFetchMethod.Post ? HttpMethod.Post : HttpMethod.Get;
        string? body = method == HttpMethod.Post ? request.Body : null;

        for (int redirects = 0; ; redirects++) {
            HttpResponseMessage response;
            try {
                using HttpRequestMessage message = CreateRequestMessage(
                    request, current, method, body, mediaType, origin, credential);
                response = await SendAsync(message, cancellationToken);
            } catch (OperationCanceledException) {
                HttpFetchFailure failure = aborted.IsCancellationRequested
                    ? HttpFetchFailure.Aborted
                    : HttpFetchFailure.Timeout;
                _ = Refuse(failure, caller, request.Url, LogLevel.Information);

                return (null, failure);
            } catch (HttpRequestException exception) {
                logger.LogInformation(exception, "Fetch for {Caller} could not reach {Host}", caller, current.IdnHost);

                return (null, HttpFetchFailure.Network);
            }

            if (!IsRedirect(response.StatusCode)) {
                return (response, HttpFetchFailure.None);
            }

            Uri? location = response.Headers.Location is { } target
                ? target.IsAbsoluteUri ? target : new Uri(current, target)
                : null;

            if (location is null || options.MaxRedirects == 0) {
                return (response, HttpFetchFailure.None);
            }

            if (redirects >= options.MaxRedirects) {
                return (response, HttpFetchFailure.TooManyRedirects);
            }

            if (!IsFetchable(location)) {
                return (response, HttpFetchFailure.InvalidRequest);
            }

            bool changesToGet = response.StatusCode is HttpStatusCode.MovedPermanently
                or HttpStatusCode.Found
                or HttpStatusCode.SeeOther;
            response.Dispose();

            if (changesToGet) {
                method = HttpMethod.Get;
                body = null;
            }

            current = location;
        }
    }

    private static HttpRequestMessage CreateRequestMessage(
        HttpFetchRequest request,
        Uri current,
        HttpMethod method,
        string? body,
        string? mediaType,
        OutboundOrigin origin,
        PageCredential? credential) {
        HttpRequestMessage message = new(method, current);
        if (body is not null) {
            message.Content = new StringContent(body, Encoding.UTF8, mediaType ?? "text/plain");
        }

        foreach ((string name, string value) in request.Headers) {
            if (!string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase)) {
                message.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (credential is not null && credential.AppliesTo(current)) {
            message.Headers.TryAddWithoutValidation(credential.Header, credential.HeaderValue);
        }

        message.Headers.TryAddWithoutValidation("User-Agent", OutboundUserAgent.For(origin));

        return message;
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

        HttpFetchFailure outcome = failure == HttpFetchFailure.None && (int)response.StatusCode >= 400
            ? HttpFetchFailure.HttpError
            : failure;

        return new HttpFetchResponse((int)response.StatusCode, ReadHeaders(response), text, outcome, truncated);
    }

    private static Dictionary<string, string> ReadHeaders(HttpResponseMessage response) {
        Dictionary<string, string> headers = [with(StringComparer.OrdinalIgnoreCase)];
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

        return headers;
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

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            if (buffer.Length > options.MaxResponseBytes) {
                break;
            }
        }

        return buffer.ToArray();
    }

    private string Decode(HttpResponseMessage response, byte[] body) {
        return EncodingFor(response).GetString(body);
    }

    private Encoding EncodingFor(HttpResponseMessage response) {
        string? charset = response.Content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrWhiteSpace(charset)) {
            try {
                return Encoding.GetEncoding(charset.Trim('"'));
            } catch (ArgumentException exception) {
                // An unknown charset is the caller's problem to spot; UTF-8 is the least surprising guess.
                logger.LogDebug(exception, "A fetched response named the unknown charset {Charset}; it was read as UTF-8", charset);
            }
        }

        return Encoding.UTF8;
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
