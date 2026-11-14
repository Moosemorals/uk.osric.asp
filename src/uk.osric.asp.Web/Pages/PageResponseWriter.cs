// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Http;

/// <summary>Applies a page's response, once, for both the buffered and the streamed path.</summary>
/// <remarks>
/// The two paths differ only in when the response starts and whether the body is written in one
/// piece. Status, content type, the page's headers, <c>no-store</c>, the script policy and the session
/// cookie are one decision and must not drift.
/// </remarks>
internal static class PageResponseWriter {

    /// <summary>Applies the response's status, content type, headers and cookies.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="response">What the page asked the response to be.</param>
    /// <param name="environment">The session the cookie belongs to.</param>
    /// <param name="session">The visitor's session for this request.</param>
    /// <param name="start">Whether to start the response after applying it.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>Whether the response is a redirect, which has no body.</returns>
    public static async Task<bool> ApplyAsync(
        HttpContext context,
        PageResponse response,
        PageEnvironment environment,
        PageSessionRequest session,
        bool start,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(session);

        if (response.Location is not null) {
            // A redirect can carry the promise of a page the visitor is not yet allowed to see, so it
            // is never cached, whoever asked.
            SecurityHeaders.NoStore(context);
            WriteSessionCookie(context, environment, session);
            Redirect.To(context, response.Status, response.Location);
            return true;
        }

        // A redirect carries no document to run a script in, so its policy is left alone.
        if (response.AllowsScripts) {
            SecurityHeaders.AllowScripts(context);
        }

        context.Response.StatusCode = response.Status;
        context.Response.ContentType = response.ContentType;
        foreach ((string name, string value) in response.Headers) {
            context.Response.Headers[name] = value;
        }

        // Every page that runs code is per-visitor: a page session can be written by any render, so
        // the body has no business in a shared cache and no business inviting a conditional request.
        // Copies first, then overrides, so a page cannot weaken it with SetHeader.
        SecurityHeaders.NoStore(context);
        context.Response.Headers.Remove("ETag");
        WriteSessionCookie(context, environment, session);

        if (start) {
            await context.Response.StartAsync(cancellationToken);
        }

        return false;
    }

    /// <summary>Whether a response with this status may carry a body.</summary>
    /// <param name="response">What the page asked the response to be.</param>
    public static bool AllowsBody(PageResponse response) {
        ArgumentNullException.ThrowIfNull(response);

        return response.Status is >= StatusCodes.Status200OK
            and not StatusCodes.Status204NoContent
            and not StatusCodes.Status304NotModified;
    }

    // Minted lazily, so a page that only reads costs a visitor no cookie and the next write is the
    // first thing that needs one.
    private static void WriteSessionCookie(
        HttpContext context,
        PageEnvironment environment,
        PageSessionRequest session) {
        if (session.NeedsCookie) {
            PageSessionCookie.Write(context, session.Id, environment.SessionCookie);
        }
    }
}
