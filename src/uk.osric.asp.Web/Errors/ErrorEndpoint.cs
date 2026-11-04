// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Errors;

using Microsoft.AspNetCore.Diagnostics;
using System.Diagnostics;
using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;

internal static class ErrorEndpoint {

    /// <summary>Writes the error page. Runs inside the exception handler's branch, not from routing.</summary>
    /// <param name="context">The failing request.</param>
    public static async Task HandleAsync(HttpContext context) {
        // The branch form of UseExceptionHandler publishes the failure under both feature names.
        IExceptionHandlerPathFeature? pathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
        Exception? exception = pathFeature?.Error
            ?? context.Features.Get<IExceptionHandlerFeature>()?.Error;

        UserFacingException? userFacingException = exception as UserFacingException;
        bool isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        bool showDetails = exception is not null
            && userFacingException is null
            && isAuthenticated;

        string message = userFacingException?.Message ?? "An unexpected error occurred. Please try again.";
        string path = pathFeature?.Path ?? context.Request.Path.Value ?? "/";

        await ShellDocument.BeginAsync(context, "Error", StatusCodes.Status500InternalServerError);
        await context.Response.WriteAsync("<h1>Something went wrong</h1>");
        await context.Response.WriteAsync($"<p>{WebUtility.HtmlEncode(message)}</p>");
        if (showDetails) {
            await context.Response.WriteAsync(EditorDetails(exception!, path));
        }
        await context.Response.WriteAsync($"<p>Reference: {WebUtility.HtmlEncode(context.TraceIdentifier)}</p>");
        if (isAuthenticated) {
            await context.Response.WriteAsync($"<p><a href=\"{WebUtility.HtmlEncode(PageUrls.Editor(PagePath.Normalize(path)))}\">Edit this page</a></p>");
        }
        await context.Response.WriteAsync("<p><a href=\"/\">Return to the home page</a></p>");
        await ShellDocument.EndAsync(context);
    }

    private static string EditorDetails(Exception exception, string path) {
        StackFrame[] frames = new StackTrace(exception, fNeedFileInfo: true).GetFrames() ?? [];
        DiagnosticLine[] locations = [.. frames
            .Where(frame => string.Equals(frame.GetFileName(), "page", StringComparison.Ordinal))
            .Select(frame => (Method: frame.GetMethod()?.Name, Line: frame.GetFileLineNumber()))
            .Where(location => location.Line > 0)
            .Select(location => {
                string method = location.Method == "Handle" ? "Handle(IContext ctx)" : "page code";
                return new DiagnosticLine($"at {method} in {path}:line {location.Line}", null, null);
            })];
        string locationList = locations.Length > 0
            ? PageLayout.Diagnostics(locations)
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
