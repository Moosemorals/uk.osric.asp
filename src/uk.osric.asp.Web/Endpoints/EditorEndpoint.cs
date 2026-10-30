// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Endpoints;

using Microsoft.Extensions.Primitives;
using System.Net;

public sealed class EditorEndpoint {

    private static readonly CookieOptions MessageCookie = new() {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = true,
        MaxAge = TimeSpan.FromMinutes(1),
        Path = "/",
    };

    public static async Task Handler(HttpContext context, IStorage storage) {

        string? path = context.Request.RouteValues["path"] as string;
        if (context.Request.Method == "POST") {
            context.Response.Cookies.Append("message", await SaveAsync(context, storage, path), MessageCookie);
            context.Response.StatusCode = 302;
            context.Response.Headers.Location = context.Request.Path.ToString();
            return;
        }

        string? content = !string.IsNullOrWhiteSpace(path) ? await storage.GetFileContentsAsync(path, context.RequestAborted) : null;

        string? message = context.Request.Cookies["message"];
        if (message is not null) {
            context.Response.Cookies.Delete("message", MessageCookie);
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

    // Every outcome redirects back to the editor with a flash message, so validation returns text
    // rather than short-circuiting the response.
    private static async Task<string> SaveAsync(HttpContext context, IStorage storage, string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return "Path is required.";
        }

        if (!context.Request.Form.TryGetValue("content", out StringValues content)) {
            return "Content is required.";
        }

        if (content.Count != 1) {
            return "Content must have exactly one value.";
        }

        await storage.SetFileContentsAsync(path, content.ToString(), context.RequestAborted);
        return "Saved successfully.";
    }

}
