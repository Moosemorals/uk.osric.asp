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

/// <summary>Writes the error page from inside the exception handler.</summary>
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
        // Async frames can repeat a line - a resumption point and the throwing statement can sit on
        // the same template line - so the projection keeps one entry per line.
        DiagnosticLine[] locations = [.. frames
            .Where(frame => string.Equals(frame.GetFileName(), "page", StringComparison.Ordinal))
            .Select(frame => (Method: frame.GetMethod(), Line: frame.GetFileLineNumber()))
            .Where(location => location.Line > 0)
            .DistinctBy(location => location.Line)
            .Select(location => {
                string method = DeclaredInHandler(location.Method)
                    ? "HandleAsync(IContext ctx)"
                    : "page code";
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

    // The handler's frame is the async state machine's MoveNext, so the method name is no use and the
    // state machine type is compiler output. Walking the declaring-type chain finds the handler for
    // the state machine, its lambdas and its local functions alike, and stops at the page's own
    // top-level types - which are exactly the ones that are not the entry point.
    private static bool DeclaredInHandler(System.Reflection.MethodBase? method) {
        for (Type? type = method?.DeclaringType; type is not null; type = type.DeclaringType) {
            if (string.Equals(type.Name, "GeneratedPage", StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }
}
