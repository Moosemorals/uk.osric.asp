// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

public sealed class EditorEndpoint() {
    public static async Task Handle(HttpContext context) {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.StatusCode = 200;

        await context.Response.WriteAsync(Framework.GetHeader("Editor"));
        await context.Response.WriteAsync("<h1>Editor Endpoint</h1>");
        await context.Response.WriteAsync(Framework.GetFooter());
    }

}