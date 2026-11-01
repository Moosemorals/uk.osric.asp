// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Endpoints;

using uk.osric.asp.Core;
using uk.osric.asp.Web.Pages;

internal sealed class MainEndpoint() {

    // The body is buffered, so a form post is read in full before the page runs; anything larger than
    // this is rejected rather than held in memory.
    private const long MaxFormBytes = 1024 * 1024;

    public static async Task Handle(
        HttpContext context,
        PageService pages,
        ILoggerFactory loggerFactory,
        ReservedPaths reserved,
        RenderOptions options) {

        PagePath path = PagePath.Normalize(context.Request.RouteValues["path"] as string);

        // A key is a path, so a decoded delimiter means the URL names no page. Checked before the
        // redirect, because redirecting to a key that cannot exist would send the browser in circles.
        if (!path.IsAddressable) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        if (!string.Equals(context.Request.Path.Value, path.Path, StringComparison.Ordinal)) {
            // The query is carried across, because the page is handed it and dropping it would change
            // what the page sees for a URL that is merely written with the wrong case.
            Redirects.Permanent(context, path.Path + context.Request.QueryString.ToUriComponent());
            return;
        }

        // A page that appeared by another route - a direct database write, an import - must not be
        // half-served for a key the site's own routing already answers for.
        if (reserved.IsReserved(path)) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        if (context.Request.ContentLength > MaxFormBytes) {
            await WriteTooLargeAsync(context, path);
            return;
        }

        StringWriter output = new();
        SimpleContext page = new(
            loggerFactory,
            PageRequestSnapshot.Create(context.Request),
            context.RequestAborted,
            output,
            options.MaxBodyChars);

        RenderResult result = await pages.RenderAsync(path, page, context.RequestAborted);

        switch (result.Status) {
            case RenderStatus.NotFound:
                await NotFound.WriteAsync(context, path);
                break;
            case RenderStatus.CompileFailed:
                await WriteCompileErrorAsync(context, result.Diagnostics);
                break;
            case RenderStatus.Timeout:
                await WriteTimeoutAsync(context, path);
                break;
            default:
                await WritePageAsync(context, page.ToResponse(), page.GetBody());
                break;
        }
    }

    // The page's body is buffered until here, so the response is written in one go: status and
    // headers first, and nothing at all when the page redirected or the request was a HEAD.
    private static async Task WritePageAsync(HttpContext context, PageResponse response, string body) {
        if (response.Location is not null) {
            Redirects.To(context, response.Status, response.Location);
            return;
        }

        context.Response.StatusCode = response.Status;
        context.Response.ContentType = response.ContentType;
        foreach ((string name, string value) in response.Headers) {
            context.Response.Headers[name] = value;
        }

        if (!HttpMethods.IsHead(context.Request.Method)) {
            await context.Response.WriteAsync(body, context.RequestAborted);
        }
    }

    private static async Task WriteCompileErrorAsync(HttpContext context, IReadOnlyList<PageDiagnostic> diagnostics) {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.StatusCode = 500;

        await context.Response.WriteAsync(Framework.GetHeader("Main - Error"));
        await context.Response.WriteAsync("<p>Handler failed to compile.</p>");
        await context.Response.WriteAsync(Framework.GetDiagnostics(diagnostics));
        await context.Response.WriteAsync(Framework.GetFooter());
    }

    private static async Task WriteTimeoutAsync(HttpContext context, PagePath path) {
        context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync($"{path} is taking too long.");
    }

    private static async Task WriteTooLargeAsync(HttpContext context, PagePath path) {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync($"{path} rejected: the request body is too large.");
    }
}
