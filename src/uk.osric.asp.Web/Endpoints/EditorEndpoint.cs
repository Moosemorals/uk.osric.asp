// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Endpoints;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Primitives;
using System.Net;
using uk.osric.asp.Core;
using uk.osric.asp.Web.Pages;

internal sealed class EditorEndpoint {

    internal const string MessageKey = "editor.message";

    public static async Task Handler(HttpContext context, PageService pages, ReservedPaths reserved, IAntiforgery antiforgery) {

        if (context.Request.Method == "POST" && context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        PagePath path = PagePath.Normalize(context.Request.RouteValues["path"] as string);
        string editorUrl = "/edit" + path.Path;

        if (context.Request.Method == "POST") {
            context.Session.SetString(MessageKey, await SaveAsync(context, pages, reserved, path));
            Redirects.SeeOther(context, editorUrl);
            return;
        }

        // A POST is saved under the canonical key and only then redirected, so a non-canonical URL can
        // never cost the edit; only a GET needs the permanent redirect to the canonical editor URL.
        if (!string.Equals(context.Request.Path.Value, editorUrl, StringComparison.Ordinal)) {
            Redirects.Permanent(context, editorUrl);
            return;
        }

        StoredPage? page = await pages.GetAsync(path, context.RequestAborted);

        string? message = context.Session.GetString(MessageKey);
        if (message is not null) {
            context.Session.Remove(MessageKey);
        }

        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.Headers.CacheControl = "no-store";

        await context.Response.WriteAsync(Framework.GetHeader("Editor"));
        await context.Response.WriteAsync($"<h1>Editing {WebUtility.HtmlEncode(path.Path)}</h1>");

        if (!string.IsNullOrEmpty(message)) {
            await context.Response.WriteAsync($"<p>{WebUtility.HtmlEncode(message)}</p>");
        }

        // A save keeps the source even when it does not compile, so the editor is where the user
        // finds out why and can fix it without losing the edit.
        if (page?.Compilation?.Diagnostics is { Count: > 0 } diagnostics) {
            await context.Response.WriteAsync(Framework.GetDiagnostics(diagnostics));
        }

        await context.Response.WriteAsync($"""
            <p><a href="{WebUtility.HtmlEncode(path.Path)}">Back to file</a></p>
            <form method="post" action="?">
                <input type="hidden" name="{WebUtility.HtmlEncode(tokens.FormFieldName)}" value="{WebUtility.HtmlEncode(tokens.RequestToken)}">
                <textarea name="content" rows="10" cols="50">{WebUtility.HtmlEncode(page?.Content ?? "")}</textarea>
                <br>
                <button type="submit">Save</button>
            </form>
            <form method="post" action="/logout">
                <input type="hidden" name="{WebUtility.HtmlEncode(tokens.FormFieldName)}" value="{WebUtility.HtmlEncode(tokens.RequestToken)}">
                <button type="submit">Sign out</button>
            </form>
        """);
        await context.Response.WriteAsync(Framework.GetFooter());
    }

    // Every outcome redirects back to the editor with a flash message, so validation returns text
    // rather than short-circuiting the response. A page that does not compile is still saved: losing
    // the edit to report the error would be the worse failure.
    private static async Task<string> SaveAsync(HttpContext context, PageService pages, ReservedPaths reserved, PagePath path) {
        if (reserved.IsReserved(path)) {
            return $"{path.Path} is reserved by the site.";
        }

        if (!context.Request.Form.TryGetValue("content", out StringValues content)) {
            return "Content is required.";
        }

        if (content.Count != 1) {
            return "Content must have exactly one value.";
        }

        IReadOnlyList<PageDiagnostic> diagnostics = await pages.SaveAsync(path, content.ToString(), context.RequestAborted);
        return diagnostics switch {
            { Count: 0 } => "Saved successfully.",
            _ when diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error) => "Saved with errors.",
            _ => "Saved with warnings.",
        };
    }
}
