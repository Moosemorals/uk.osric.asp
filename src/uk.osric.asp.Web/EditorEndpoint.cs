// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using Microsoft.Extensions.Primitives;
using System.Net;

public sealed class EditorEndpoint {

    public static async Task Handler(HttpContext context, IStorage storage) {

        string? path = context.Request.RouteValues["path"] as string;
        if (context.Request.Method == "POST") {
            if (string.IsNullOrWhiteSpace(path)) {
                context.Response.Cookies.Append("message", "Path is required.");
                goto done;
            }

            if (!context.Request.Form.TryGetValue("content", out StringValues content)) {
                context.Response.Cookies.Append("message", "Content is required.");
                goto done;
            }

            if (content.Count != 1) {
                context.Response.Cookies.Append("message", "Content must have exactly one value.");
                goto done;
            }

            await storage.SetFileContentsAsync(path, content.ToString(), context.RequestAborted);
            context.Response.Cookies.Append("message", "Saved successfully.");

        done:

            context.Response.StatusCode = 302;
            context.Response.Headers.Location = context.Request.Path.ToString();
            return;

        } else {

            string? content = !string.IsNullOrWhiteSpace(path) ? await storage.GetFileContentsAsync(path, context.RequestAborted) : null;

            string? message = context.Request.Cookies.FirstOrDefault(c => c.Key == "message").Value;
            if (message is not null) {
                context.Response.Cookies.Delete("message");
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.StatusCode = 200;

            await context.Response.WriteAsync(Framework.GetHeader("Editor"));
            await context.Response.WriteAsync($"<h1>Editing {WebUtility.HtmlEncode(path)}</h1>");
            await context.Response.WriteAsync($"""
            {(!string.IsNullOrEmpty(message) ? $"<p>{WebUtility.HtmlEncode(message)}</p>" : "")}
            <p><a href="/{WebUtility.HtmlEncode(path)}">Back to file</a></p>
            <form method="post" action="?">
                <textarea name="content" rows="10" cols="50">{WebUtility.HtmlEncode(content ?? "")}</textarea>
                <br>
                <button type="submit">Save</button>
            </form>
        """);
            await context.Response.WriteAsync(Framework.GetFooter());

        }
    }

}
