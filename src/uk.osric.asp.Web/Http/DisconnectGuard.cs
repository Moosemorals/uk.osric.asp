// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Http;

/// <summary>Turns a client that left mid-write into a quiet end rather than an unhandled exception.</summary>
/// <remarks>
/// Registered inside <c>UseExceptionHandler</c>, because that handler logs an unhandled exception
/// before it invokes its branch. A disconnect is not a failed request: the run or the page may still
/// be working, and nothing could have been sent to the client anyway.
/// </remarks>
internal static class DisconnectGuard {

    /// <summary>Adds the guard to the pipeline.</summary>
    /// <param name="app">The pipeline, after the exception handler and before the endpoints.</param>
    public static IApplicationBuilder UseDisconnectGuard(this IApplicationBuilder app) {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) => {
            try {
                await next(context);
            } catch (Exception exception) when (ResponseWrite.IsDisconnect(context, exception)) {
                // The access log and the request scope already name the request; the exception handler
                // would call this a failed request and try to write a page the response cannot take.
                context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(DisconnectGuard).FullName!)
                    .LogDebug(exception, "The client left before the response for {Path} finished; the request was abandoned",
                        context.Request.Path);

                context.Abort();
            }
        });
    }
}
