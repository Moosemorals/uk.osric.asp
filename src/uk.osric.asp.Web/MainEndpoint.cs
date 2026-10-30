// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using Microsoft.CodeAnalysis;
using uk.osric.asp.Core;

public sealed class MainEndpoint() {

    public static async Task Handle(HttpContext context, IStorage storage) {
        

        string path = context.Request.RouteValues["path"] as string ?? "";

        string? content = !string.IsNullOrEmpty(path) ? await storage.GetFileContentsAsync(path, context.RequestAborted) : null;
        if (content is null) {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("File not found.");
            return;
        }


        IContext ctx = new SimpleContext();

        MemoryStream assemblyStream = new();
        ctx.BuildResult = TemplateCompiler.Compile(content, assemblyStream);

        if (ctx.BuildResult.Success) {
            assemblyStream.Seek(0, SeekOrigin.Begin);
            TemplateCompiler.Run(assemblyStream, ctx);

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync(Framework.GetHeader("Main"));
            await context.Response.WriteAsync("<h1>Handler executed successfully.</h1>");
            await context.Response.WriteAsync(Framework.GetFooter());
        } else {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync(Framework.GetHeader("Main - Error"));
            await context.Response.WriteAsync("Handler failed to compile.");
            foreach (Diagnostic diagnostic in ctx.BuildResult.Diagnostics) {
                await context.Response.WriteAsync(diagnostic.ToString() + "\n");
            }
            await context.Response.WriteAsync(Framework.GetFooter());
        }
    }
}
