// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Http;

/// <summary>Recognises a write that failed because the client or the response had already gone.</summary>
/// <remarks>
/// The exception handler logs an unhandled exception before it tries to write its error page. A
/// response that has already started cannot take that page, and a client that has left cannot take
/// anything, so both must be recognised before the handler sees them.
/// </remarks>
internal static class ResponseWrite {

    /// <summary>Whether an exception is the response's own end rather than a fault.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="exception">The exception the write raised.</param>
    public static bool IsDisconnect(HttpContext context, Exception exception) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);

        if (context.RequestAborted.IsCancellationRequested && exception is OperationCanceledException) {
            return true;
        }

        // The page sink wraps a failed write in a cancellation so the renderer stops the page; the
        // response has still started and the inner exception is the body's own end.
        if (exception is OperationCanceledException { InnerException: { } inner }) {
            return IsDisconnect(context, inner);
        }

        // Once the response has started, a body that throws these cannot be written to again. The
        // distinction from a live failure is not observable from here, and the alternative is
        // reporting a browser reload as an unhandled exception (BUG-17).
        return context.Response.HasStarted
            && exception is NotSupportedException or IOException or ObjectDisposedException;
    }
}
