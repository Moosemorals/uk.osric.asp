// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Endpoints;

using System.Net;
using uk.osric.asp.Core;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web;

public sealed class MainEndpoint() {

    public static async Task Handle(HttpContext context, IStorage storage, ILogger<MainEndpoint> logger) {
        string path = context.Request.RouteValues["path"] as string ?? "";

        string? content = !string.IsNullOrWhiteSpace(path) ? await storage.GetFileContentsAsync(path, context.RequestAborted) : null;
        if (content is null) {
            await WriteNotFoundAsync(context);
            return;
        }

        string templateSource = TemplateSourceGenerator.GenerateTemplateSource(content);
        StringWriter output = new();

        IContext ctx = new SimpleContext(logger, output, path);

        using MemoryStream assemblyStream = new();
        CompileResult buildResult = TemplateCompiler.Compile(templateSource, assemblyStream);

        if (buildResult.Success) {
            assemblyStream.Seek(0, SeekOrigin.Begin);

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.StatusCode = 200;

            TemplateCompiler.Run(assemblyStream, ctx);
            await context.Response.WriteAsync(output.ToString());
        } else {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync(Framework.GetHeader("Main - Error"));
            await context.Response.WriteAsync("<p>Handler failed to compile.</p><pre>");
            foreach (string diagnostic in buildResult.Diagnostics) {
                await context.Response.WriteAsync(WebUtility.HtmlEncode(diagnostic));
                await context.Response.WriteAsync("\n");
            }
            await context.Response.WriteAsync("</pre>");
            await context.Response.WriteAsync(Framework.GetFooter());
        }
    }

    private static async Task WriteNotFoundAsync(HttpContext context) {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("File not found.");
    }
}
