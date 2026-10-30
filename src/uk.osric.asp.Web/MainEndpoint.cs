// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using Microsoft.CodeAnalysis;
using uk.osric.asp.Core;

public sealed class MainEndpoint() {

    public static async Task Handle(HttpContext context) {
        IContext ctx = new SimpleContext();

        MemoryStream assemblyStream = new();
        ctx.BuildResult = Compiler.Compile("""
            using uk.osric.asp.Core;
            public class Hello : IHandler {

                public void Handle(IContext context) {
                    context.Log("Hello, world!");
                }
            }
        """, assemblyStream);

        if (ctx.BuildResult.Success) {
            assemblyStream.Seek(0, SeekOrigin.Begin);
            Compiler.Run(assemblyStream, ctx);

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