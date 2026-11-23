// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Shell;

using System.Globalization;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

/// <summary>The route that serves a stored object by its digest.</summary>
/// <remarks>
/// <para>
/// A page cannot stream bytes and the cache policy is a response rule, so the route is the site's own
/// code. It streams the BLOB in chunks from SQLite, honours a single byte range and a conditional
/// request, and answers a page subject's object as public and immutable and a visitor subject's as
/// private and uncacheable.
/// </para>
/// <para>
/// A digest that names nothing, or a visitor object the reader does not hold, is a <c>404</c> rather
/// than a distinct answer, so the route cannot be used to probe another visitor's existence.
/// </para>
/// </remarks>
internal static class ObjectEndpoint {

    private const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    private const string PrivateCacheControl = "private, no-store";

    private const int ChunkBytes = 64 * 1024;

    /// <summary>Answers <c>GET</c> and <c>HEAD</c> for <c>/o/{sha}</c>.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="objects">Where the object is opened.</param>
    /// <param name="environment">The session and browser identities the reader is resolved from.</param>
    /// <param name="sha">The digest in the URL.</param>
    public static async Task HandleAsync(
        HttpContext context,
        IStoreObjects objects,
        PageEnvironment environment,
        string sha) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(environment);

        if (!PageObjectHash.IsCanonical(sha)) {
            await NotFoundAsync(context);
            return;
        }

        PageUser? user = PageUserMapping.From(context.User);
        PageSessionRequest session = await PageSessionResolver.ResolveAsync(
            context, environment.SessionStore, environment.SessionCookie, user, context.RequestAborted);
        PageReaderSubject reader = PageReaderSubjectResolver.Resolve(
            context, session, environment.Stores.Visitor, environment.BrowserCookie);

        IServedObject? source = await objects.OpenServedAsync(
            sha, reader.ReadSubjects, context.RequestAborted);

        if (source is null) {
            await NotFoundAsync(context);
            return;
        }

        // A visitor opening their object is a read by that visitor, so it slides the same idle clock
        // a page's read does. A HEAD does not slide it, and a missing object does not either, because
        // a probe is not a visit.
        if (!HttpMethods.IsHead(context.Request.Method)) {
            await session.TouchAsync(environment.SessionStore, context.RequestAborted);
        }

        await using (source) {
            await WriteAsync(context, source);
        }
    }

    private static async Task WriteAsync(HttpContext context, IServedObject source) {
        string etag = "\"" + source.Sha + "\"";
        context.Response.Headers.ETag = etag;
        context.Response.Headers.AcceptRanges = "bytes";
        context.Response.ContentType = source.ContentType;
        context.Response.Headers.CacheControl = source.Public ? ImmutableCacheControl : PrivateCacheControl;

        if (MatchesIfNoneMatch(context.Request.Headers.IfNoneMatch, etag)) {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        ByteRange? range = ParseRange(context.Request.Headers.Range, source.Length, out bool unsatisfiable);
        if (unsatisfiable) {
            SecurityHeaders.NoStore(context);
            context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            context.Response.Headers.ContentRange =
                $"bytes */{source.Length.ToString(CultureInfo.InvariantCulture)}";
            context.Response.ContentLength = 0;
            return;
        }

        long start = range?.Start ?? 0;
        long count = range?.Count ?? source.Length;

        if (range is not null) {
            context.Response.StatusCode = StatusCodes.Status206PartialContent;
            long last = start + count - 1;
            context.Response.Headers.ContentRange = string.Create(
                CultureInfo.InvariantCulture,
                $"bytes {start}-{last}/{source.Length}");
        } else {
            context.Response.StatusCode = StatusCodes.Status200OK;
        }

        context.Response.ContentLength = count;

        if (HttpMethods.IsHead(context.Request.Method) || count == 0) {
            return;
        }

        await WriteBodyAsync(source, start, count, context.Response.Body, context.RequestAborted);
    }

    private static async Task WriteBodyAsync(
        IServedObject source,
        long start,
        long count,
        Stream destination,
        CancellationToken cancellationToken) {
        byte[] buffer = new byte[(int)Math.Min(ChunkBytes, count)];
        long offset = start;
        long remaining = count;

        while (remaining > 0) {
            int wanted = (int)Math.Min(buffer.Length, remaining);
            int read = await source.ReadAtAsync(buffer.AsMemory(0, wanted), offset, cancellationToken);
            if (read <= 0) {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            offset += read;
            remaining -= read;
        }
    }

    // One range is answered; a list of them is legal but the route serves the whole object instead,
    // which is what a server may do and keeps the response a single byte range.
    private static ByteRange? ParseRange(string? header, long length, out bool unsatisfiable) {
        unsatisfiable = false;

        if (string.IsNullOrWhiteSpace(header)
            || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        string[] parts = header["bytes=".Length..].Split(',');
        if (parts.Length != 1) {
            return null;
        }

        string part = parts[0].Trim();
        int dash = part.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0) {
            return null;
        }

        string first = part[..dash].Trim();
        string last = part[(dash + 1)..].Trim();

        if (first.Length == 0) {
            if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out long suffix)) {
                return null;
            }

            if (suffix == 0 || length == 0) {
                unsatisfiable = true;
                return null;
            }

            return new ByteRange(Math.Max(0, length - suffix), Math.Min(suffix, length));
        }

        if (!long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out long start)) {
            return null;
        }

        if (start >= length) {
            unsatisfiable = true;
            return null;
        }

        if (last.Length == 0) {
            return new ByteRange(start, length - start);
        }

        if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out long end) || end < start) {
            unsatisfiable = true;
            return null;
        }

        long capped = Math.Min(end, length - 1);

        return new ByteRange(start, capped - start + 1);
    }

    private static bool MatchesIfNoneMatch(string? header, string etag) {
        if (string.IsNullOrWhiteSpace(header)) {
            return false;
        }

        foreach (string value in header.Split(',')) {
            string candidate = value.Trim();

            if (string.Equals(candidate, "*", StringComparison.Ordinal)
                || string.Equals(candidate, etag, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private static Task NotFoundAsync(HttpContext context) {
        SecurityHeaders.NoStore(context);
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentLength = 0;

        return Task.CompletedTask;
    }

    private readonly record struct ByteRange(long Start, long Count);
}
