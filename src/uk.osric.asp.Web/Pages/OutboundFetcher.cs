// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Net.Http.Headers;
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
    /// <param name="credential">A credential to attach where it applies, or <see langword="null"/> for none.</param>
    /// <param name="aborted">Cancelled when the caller gives up.</param>
    /// <param name="deadline">Cancelled when the caller's budget expires.</param>
    /// <returns>The response as a page reads it: text, and the headers a page may see.</returns>
    public async Task<HttpFetchResponse> FetchAsync(
        HttpFetchRequest request,
        string caller,
        OutboundOrigin origin,
        IOutboundCredential? credential,
        CancellationToken aborted,
        CancellationToken deadline) {
        OutboundResponse response = await SendAndReadAsync(request, caller, origin, credential, aborted, deadline);

        return Project(response);
    }

    /// <summary>Performs one request and answers the transport's own result.</summary>
    /// <param name="request">The request to make.</param>
    /// <param name="caller">Who asked, for the log line: a page key or a tool name.</param>
    /// <param name="origin">Whether page code or the assistant asked, for the <c>User-Agent</c>.</param>
    /// <param name="credential">A credential to attach where it applies, or <see langword="null"/> for none.</param>
    /// <param name="aborted">Cancelled when the caller gives up.</param>
    /// <param name="deadline">Cancelled when the caller's budget expires.</param>
    /// <returns>Every header, the body's bytes and the hops taken, for a caller that wants the response itself.</returns>
    public Task<OutboundResponse> FetchResponseAsync(
        HttpFetchRequest request,
        string caller,
        OutboundOrigin origin,
        IOutboundCredential? credential,
        CancellationToken aborted,
        CancellationToken deadline) {
        return SendAndReadAsync(request, caller, origin, credential, aborted, deadline);
    }

    private async Task<OutboundResponse> SendAndReadAsync(
        HttpFetchRequest request,
        string caller,
        OutboundOrigin origin,
        IOutboundCredential? credential,
        CancellationToken aborted,
        CancellationToken deadline) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);

        List<OutboundRedirect> redirects = [];
        if (!TryPrepareRequest(request, caller, credential, out Uri? current, out string? mediaType)) {
            return Empty(HttpFetchFailure.InvalidRequest, redirects);
        }

        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(aborted, deadline);
        budget.CancelAfter(options.Timeout);
        (HttpResponseMessage? response, HttpFetchFailure failure) = await SendUntilResponseAsync(
            request, caller, origin, credential, current, mediaType, redirects, aborted, budget.Token);
        if (response is null) {
            return Empty(failure, redirects);
        }

        using (response) {
            try {
                return await ReadAsync(response, failure, redirects, budget.Token);
            } catch (OperationCanceledException) {
                return Empty(
                    Refuse(
                        aborted.IsCancellationRequested ? HttpFetchFailure.Aborted : HttpFetchFailure.Timeout,
                        caller,
                        request.Url,
                        LogLevel.Information),
                    redirects);
            } catch (IOException exception) {
                logger.LogInformation(exception, "Fetch for {Caller} lost its response body from {Host}", caller, current.IdnHost);

                return Empty(HttpFetchFailure.Network, redirects);
            }
        }
    }

    /// <summary>Opens a bounded text stream for one request.</summary>
    /// <param name="request">The request to make.</param>
    /// <param name="caller">Who asked, for the log line.</param>
    /// <param name="origin">Whether page code or the assistant asked, for the <c>User-Agent</c>.</param>
    /// <param name="credential">A credential to attach where it applies, or <see langword="null"/> for none.</param>
    /// <param name="aborted">Cancelled when the caller gives up.</param>
    /// <param name="deadline">Cancelled when the caller's lifetime ends.</param>
    /// <param name="onClosed">Records the completed stream.</param>
    public async Task<HttpFetchStream> StreamAsync(
        HttpFetchRequest request,
        string caller,
        OutboundOrigin origin,
        IOutboundCredential? credential,
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
        // A stream reports its status and headers, not the hops it took, so the list is thrown away.
        (HttpResponseMessage? response, HttpFetchFailure failure) = await SendUntilResponseAsync(
            request, caller, origin, credential, current, mediaType, hops: [], aborted, headerBudget.Token);
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
                EncodingFor(response.Content.Headers.ContentType?.ToString()),
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

    /// <summary>Judges one request without sending it, so a caller can charge for it only if it would be attempted.</summary>
    /// <param name="request">The request to judge.</param>
    /// <param name="credential">The credential the request would carry, or <see langword="null"/> for none.</param>
    /// <param name="reason">Why the request would be refused, for the caller's own log line.</param>
    /// <returns><see langword="true"/> when the transport would attempt the request.</returns>
    /// <remarks>
    /// These are the same rules <see cref="SendAndReadAsync"/> applies before it opens a socket, asked
    /// separately so the answer costs nothing. It logs nothing, because a caller that charges for
    /// the attempt owns the one line a refusal is worth.
    /// </remarks>
    public static bool TryValidate(HttpFetchRequest request, IOutboundCredential? credential, out string reason) {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryCreateUri(request.Url, out Uri? current)) {
            reason = "the URL is not an absolute http or https URL with no credentials and no fragment";

            return false;
        }

        if (!TryValidateShape(request, out _)) {
            reason = "the method, body or a header is not one the page contract allows";

            return false;
        }

        if (credential is not null && !credential.AppliesTo(current)) {
            reason = $"{credential.Name} is not the credential's host or is not secure";

            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool TryPrepareRequest(
        HttpFetchRequest request,
        string caller,
        IOutboundCredential? credential,
        out Uri current,
        out string? mediaType) {
        mediaType = null;
        if (!TryCreateUri(request.Url, out Uri? parsed) || !TryValidateShape(request, out mediaType)) {
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
        IOutboundCredential? credential,
        Uri current,
        string? mediaType,
        List<OutboundRedirect> hops,
        CancellationToken aborted,
        CancellationToken cancellationToken) {
        HttpMethod method = request.Method == HttpFetchMethod.Post ? HttpMethod.Post : HttpMethod.Get;
        string? body = method == HttpMethod.Post ? request.Body : null;

        for (int followed = 0; ; followed++) {
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

            if (followed >= options.MaxRedirects) {
                return (response, HttpFetchFailure.TooManyRedirects);
            }

            if (!IsFetchable(location)) {
                return (response, HttpFetchFailure.InvalidRequest);
            }

            bool changesToGet = response.StatusCode is HttpStatusCode.MovedPermanently
                or HttpStatusCode.Found
                or HttpStatusCode.SeeOther;

            // A hop is recorded only when it is taken: a redirect the limit or the scheme rule stops
            // is answered by the 3xx itself, whose Location header says the same thing.
            hops.Add(new OutboundRedirect((int)response.StatusCode, location.AbsoluteUri));
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
        IOutboundCredential? credential) {
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

        message.Headers.TryAddWithoutValidation("User-Agent", origin.UserAgent());

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

    private static bool TryValidateShape(HttpFetchRequest request, out string? mediaType) {
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

    private async Task<OutboundResponse> ReadAsync(
        HttpResponseMessage response,
        HttpFetchFailure failure,
        IReadOnlyList<OutboundRedirect> redirects,
        CancellationToken cancellationToken) {
        byte[] body = await ReadBodyAsync(response, cancellationToken);
        bool truncated = body.Length > options.MaxResponseBytes;
        if (truncated) {
            body = body[..options.MaxResponseBytes];
        }

        HttpFetchFailure outcome = failure == HttpFetchFailure.None && (int)response.StatusCode >= 400
            ? HttpFetchFailure.HttpError
            : failure;

        return new OutboundResponse(
            (int)response.StatusCode,
            ReadAllHeaders(response),
            body,
            outcome,
            truncated,
            redirects);
    }

    // Everything the response carried. A page is shown a subset of this (ReadHeaders), but a caller
    // that is checking what the server produced needs the headers it did not think to ask for.
    private static Dictionary<string, string> ReadAllHeaders(HttpResponseMessage response) {
        Dictionary<string, string> headers = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers) {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers) {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        return headers;
    }

    // The page's view of a response, which is a decision rather than a transport fact: nine headers it
    // may see, and the body as text decoded by the charset the response declares.
    private HttpFetchResponse Project(OutboundResponse response) {
        Dictionary<string, string> headers = [with(StringComparer.OrdinalIgnoreCase)];
        foreach ((string name, string value) in response.Headers) {
            if (PageFetchHeaders.IsResponseAllowed(name)) {
                headers[name] = value;
            }
        }

        string text = Decode(headers.GetValueOrDefault("Content-Type"), response.Body);

        return new HttpFetchResponse(response.Status, headers, text, response.Failure, response.Truncated);
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

    // Content-Length is not trusted to be small, so the limit is applied to what arrives.
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

    private string Decode(string? contentType, byte[] body) {
        return EncodingFor(contentType).GetString(body);
    }

    private Encoding EncodingFor(string? contentType) {
        if (contentType is not null
            && MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? parsed)
            && parsed.Charset.HasValue) {
            string charset = parsed.Charset.Value.Trim('"');
            try {
                return Encoding.GetEncoding(charset);
            } catch (ArgumentException exception) {
                // An unknown charset is the caller's problem to spot; UTF-8 is the least surprising guess.
                logger.LogDebug(exception, "A fetched response named the unknown charset {Charset}; it was read as UTF-8", charset);
            }
        }

        return Encoding.UTF8;
    }

    private HttpFetchFailure Refuse(HttpFetchFailure failure, string caller, string url, LogLevel level) {
        // The host, not the URL: a query can carry a secret, and the host name is enough to explain
        // the refusal.
        string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? parsed.IdnHost : "(unparsed)";
        logger.Log(level, "Fetch for {Caller} was refused a request to {Host}: {Failure}", caller, host, failure);

        return failure;
    }

    private static OutboundResponse Empty(HttpFetchFailure failure, IReadOnlyList<OutboundRedirect> redirects) {
        return new OutboundResponse(0, new Dictionary<string, string>(), [], failure, false, redirects);
    }
}
