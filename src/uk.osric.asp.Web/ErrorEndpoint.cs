// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using Microsoft.AspNetCore.Diagnostics;
using System.Net;
using System.Reflection;
using uk.osric.asp.Core;

public static class ErrorEndpoint {

    /// <summary>Writes the error page. Runs inside the exception handler's branch, not from routing.</summary>
    /// <param name="context">The failing request.</param>
    public static async Task HandleAsync(HttpContext context) {
        // The branch form of UseExceptionHandler publishes the failure under both feature names.
        Exception? exception = context.Features.Get<IExceptionHandlerPathFeature>()?.Error
            ?? context.Features.Get<IExceptionHandlerFeature>()?.Error;

        UserFacingException? userFacingException = exception switch {
            UserFacingException safeException => safeException,
            TargetInvocationException { InnerException: UserFacingException safeException } => safeException,
            _ => null
        };

        string message = userFacingException?.Message ?? "An unexpected error occurred. Please try again.";
        string page = Framework.GetHeader("Error") + $"""
                <main>
                    <h1>Something went wrong</h1>
                    <p>{WebUtility.HtmlEncode(message)}</p>
                    <p>Reference: {WebUtility.HtmlEncode(context.TraceIdentifier)}</p>
                    <p><a href="/">Return to the home page</a></p>
                </main>
        """ + Framework.GetFooter();

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(page);
    }
}
