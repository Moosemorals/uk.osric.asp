// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Http;

/// <summary>The one response sink a streaming page writes into.</summary>
internal sealed class PageBodySink(
    HttpContext context,
    PageEnvironment environment,
    PageSessionRequest session) : IPageBodySink {

    private bool _bodyAllowed;

    /// <inheritdoc/>
    public async Task StartAsync(PageResponse response, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(response);

        if (await PageResponseWriter.ApplyAsync(context, response, environment, session, start: true, cancellationToken)) {
            throw new InvalidOperationException("A redirect has no body to stream.");
        }

        _bodyAllowed = PageResponseWriter.AllowsBody(response);
    }

    /// <inheritdoc/>
    public async Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) {
        if (!_bodyAllowed || bytes.IsEmpty || HttpMethods.IsHead(context.Request.Method)) {
            return;
        }

        try {
            await context.Response.Body.WriteAsync(bytes, cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        } catch (Exception exception) when (ResponseWrite.IsDisconnect(context, exception)) {
            // The page cannot be stopped, but its next write must not reach a response that is gone.
            // The renderer treats this as the client leaving rather than as a page fault.
            throw new OperationCanceledException(
                $"{context.Request.Path} lost the client while its response was being written.", exception);
        }
    }
}
