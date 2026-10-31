// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Endpoints;

using Microsoft.Extensions.Primitives;
using System.Net;
using uk.osric.asp.Core;

public sealed class EditorEndpoint {

    public static async Task Handler(HttpContext context, IStorage storage, IWebHostEnvironment environment) {

        PagePath path = PagePath.Normalize(context.Request.RouteValues["path"] as string);
        string editorUrl = "/edit" + path.Path;
        CookieOptions cookie = CreateMessageCookie(environment);

        if (context.Request.Method == "POST") {
            context.Response.Cookies.Append("message", await SaveAsync(context, storage, path), cookie);
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = new PathString(editorUrl).ToUriComponent();
            return;
        }

        // A POST is saved under the canonical key and only then redirected, so a non-canonical URL can
        // never cost the edit; only a GET needs the permanent redirect to the canonical editor URL.
        if (!string.Equals(context.Request.Path.Value, editorUrl, StringComparison.Ordinal)) {
            Redirects.Permanent(context, editorUrl);
            return;
        }

        string? content = await storage.GetFileContentsAsync(path, context.RequestAborted);

        string? message = context.Request.Cookies["message"];
        if (message is not null) {
            context.Response.Cookies.Delete("message", cookie);
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.StatusCode = 200;

        await context.Response.WriteAsync(Framework.GetHeader("Editor"));
        await context.Response.WriteAsync($"<h1>Editing {WebUtility.HtmlEncode(path.Path)}</h1>");
        await context.Response.WriteAsync($"""
            {(!string.IsNullOrEmpty(message) ? $"<p>{WebUtility.HtmlEncode(message)}</p>" : "")}
            <p><a href="{WebUtility.HtmlEncode(path.Path)}">Back to file</a></p>
            <form method="post" action="?">
                <textarea name="content" rows="10" cols="50">{WebUtility.HtmlEncode(content ?? "")}</textarea>
                <br>
                <button type="submit">Save</button>
            </form>
        """);
        await context.Response.WriteAsync(Framework.GetFooter());
    }

    // Secure whenever the host is production, whatever the request says: Caddy terminates TLS, and a
    // missing or forged forwarded header must not weaken the cookie. Local Development over http
    // still receives the flash message.
    private static CookieOptions CreateMessageCookie(IWebHostEnvironment environment) => new() {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = environment.IsProduction(),
        MaxAge = TimeSpan.FromMinutes(1),
        Path = "/",
    };

    // Every outcome redirects back to the editor with a flash message, so validation returns text
    // rather than short-circuiting the response.
    private static async Task<string> SaveAsync(HttpContext context, IStorage storage, PagePath path) {
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
