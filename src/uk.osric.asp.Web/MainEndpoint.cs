// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using uk.osric.asp.Core;
using uk.osric.asp.Runtime;

public sealed class MainEndpoint() {

    public static async Task Handle(HttpContext context, IStorage storage, ILogger<MainEndpoint> logger) {
        string path = context.Request.RouteValues["path"] as string ?? "";

        string? content = !string.IsNullOrEmpty(path) ? await storage.GetFileContentsAsync(path, context.RequestAborted) : null;
        if (content is null) {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("File not found.");
            return;
        }

        string templateSource = TemplateSourceGenerator.GenerateTemplateSource(content);
        StringWriter output = new();

        IContext ctx = new SimpleContext(logger, output, path);

        MemoryStream assemblyStream = new();
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
            await context.Response.WriteAsync("Handler failed to compile.");
            foreach (string diagnostic in buildResult.Diagnostics) {
                await context.Response.WriteAsync(diagnostic + "\n");
            }
            await context.Response.WriteAsync(Framework.GetFooter());
        }
    }
}
