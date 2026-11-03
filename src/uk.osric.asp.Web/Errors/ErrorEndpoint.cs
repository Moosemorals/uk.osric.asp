// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Errors;

using Microsoft.AspNetCore.Diagnostics;
using System.Diagnostics;
using System.Net;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;

internal static class ErrorEndpoint {

    /// <summary>Writes the error page. Runs inside the exception handler's branch, not from routing.</summary>
    /// <param name="context">The failing request.</param>
    public static async Task HandleAsync(HttpContext context) {
        // The branch form of UseExceptionHandler publishes the failure under both feature names.
        IExceptionHandlerPathFeature? pathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
        Exception? exception = pathFeature?.Error
            ?? context.Features.Get<IExceptionHandlerFeature>()?.Error;

        UserFacingException? userFacingException = exception as UserFacingException;
        bool showDetails = exception is not null
            && userFacingException is null
            && context.User.Identity?.IsAuthenticated == true;

        string message = userFacingException?.Message ?? "An unexpected error occurred. Please try again.";
        string details = showDetails
            ? EditorDetails(exception!, pathFeature?.Path ?? context.Request.Path.Value ?? "/")
            : string.Empty;
        string page = PageLayout.Header("Error") + $"""
                <main>
                    <h1>Something went wrong</h1>
                    <p>{WebUtility.HtmlEncode(message)}</p>
                    {details}
                    <p>Reference: {WebUtility.HtmlEncode(context.TraceIdentifier)}</p>
                    <p><a href="/">Return to the home page</a></p>
                </main>
        """ + PageLayout.Footer();

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/html; charset=utf-8";
        if (showDetails) {
            context.Response.Headers.CacheControl = "no-store";
        }

        await context.Response.WriteAsync(page);
    }

    private static string EditorDetails(Exception exception, string path) {
        StackFrame[] frames = new StackTrace(exception, fNeedFileInfo: true).GetFrames() ?? [];
        string locations = string.Concat(frames
            .Where(frame => string.Equals(frame.GetFileName(), "page", StringComparison.Ordinal))
            .Select(frame => (Method: frame.GetMethod()?.Name, Line: frame.GetFileLineNumber()))
            .Where(location => location.Line > 0)
            .Select(location => {
                string method = location.Method == "Handle" ? "Handle(IContext ctx)" : "page code";
                return $"at {method} in {WebUtility.HtmlEncode(path)}:line {location.Line}\n";
            }));
        string locationList = locations.Length > 0
            ? $"<pre class=\"diagnostics\">{locations}</pre>"
            : "<p>No template location is available.</p>";

        return $"""
                <section>
                    <h2>Page details</h2>
                    <p>{WebUtility.HtmlEncode(exception.GetType().Name)}: {WebUtility.HtmlEncode(exception.Message)}</p>
                    {locationList}
                </section>
        """;
    }
}
