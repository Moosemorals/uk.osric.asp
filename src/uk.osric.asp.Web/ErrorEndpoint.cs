// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using Microsoft.AspNetCore.Diagnostics;
using System.Net;
using System.Reflection;
using uk.osric.asp.Core;

internal static class ErrorEndpoint {
    public static IResult Handle(HttpContext context) {
        Exception? exception = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
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

        return Results.Content(page, "text/html");
    }
}
