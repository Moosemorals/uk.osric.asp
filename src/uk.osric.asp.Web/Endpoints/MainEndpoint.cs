// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Endpoints;

using uk.osric.asp.Core;
using uk.osric.asp.Web.Pages;

public sealed class MainEndpoint() {

    public static async Task Handle(HttpContext context, PageService pages, ILogger<MainEndpoint> logger) {

        PagePath path = PagePath.Normalize(context.Request.RouteValues["path"] as string);
        if (!string.Equals(context.Request.Path.Value, path.Path, StringComparison.Ordinal)) {
            Redirects.Permanent(context, path.Path);
            return;
        }

        StringWriter output = new();
        IContext ctx = new SimpleContext(logger, output, path.Path);
        RenderResult result = await pages.RenderAsync(path, ctx, context.RequestAborted);

        switch (result.Status) {
            case RenderStatus.NotFound:
                await WriteNotFoundAsync(context, path);
                break;
            case RenderStatus.CompileFailed:
                await WriteCompileErrorAsync(context, result.Diagnostics);
                break;
            default:
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync(output.ToString());
                break;
        }
    }

    private static async Task WriteCompileErrorAsync(HttpContext context, IReadOnlyList<string> diagnostics) {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.StatusCode = 500;

        await context.Response.WriteAsync(Framework.GetHeader("Main - Error"));
        await context.Response.WriteAsync("<p>Handler failed to compile.</p>");
        await context.Response.WriteAsync(Framework.GetDiagnostics(diagnostics));
        await context.Response.WriteAsync(Framework.GetFooter());
    }

    private static async Task WriteNotFoundAsync(HttpContext context, PagePath path) {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync($"{path} not found.");
    }
}
