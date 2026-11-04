// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Primitives;
using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

internal static class AdminRestoreEndpoint {

    public static async Task GetAsync(HttpContext context, IAntiforgery antiforgery) {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryGetPath(context.Request.Query["path"], out PagePath path)) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!path.IsAddressable) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        string? content = await PageAssetSeeder.ReadSeedContentAsync(path, context.RequestAborted);
        if (content is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        await WriteConfirmationAsync(context, path, content, tokens);
    }

    public static async Task PostAsync(HttpContext context, PageEditor pages) {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }
            || !context.Request.HasFormContentType) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted);
        if (!form.TryGetValue("path", out StringValues pathValues)
            || !TryGetPath(pathValues, out PagePath path)) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!path.IsAddressable) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        string? content = await PageAssetSeeder.ReadSeedContentAsync(path, context.RequestAborted);
        if (content is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        await pages.SaveAsync(path, content, context.RequestAborted);
        context.Session.SetString(EditorEndpoint.MessageKey, "Restored the bundled version.");
        Redirects.SeeOther(context, EditorUrl(path));
    }

    private static bool TryGetPath(StringValues values, out PagePath path) {
        path = PagePath.Normalize(null);
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0])) {
            return false;
        }

        path = PagePath.Normalize(values[0]);
        return true;
    }

    private static async Task WriteConfirmationAsync(
        HttpContext context,
        PagePath path,
        string content,
        AntiforgeryTokenSet tokens) {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(PageLayout.Header("Restore bundled version", stylesheet: "/editor.css"));
        await context.Response.WriteAsync($"<h1>Restore {WebUtility.HtmlEncode(path.Key)}</h1>");
        await context.Response.WriteAsync(
            $"<p>This will replace the current page content with the version bundled with this release. "
            + "The existing revision history will be retained.</p>");
        await context.Response.WriteAsync($"<pre class=\"source\">{WebUtility.HtmlEncode(content)}</pre>");
        await context.Response.WriteAsync($"""
            <form method="post" action="/admin/restore">
                <input type="hidden" name="{WebUtility.HtmlEncode(tokens.FormFieldName)}" value="{WebUtility.HtmlEncode(tokens.RequestToken)}">
                <input type="hidden" name="path" value="{WebUtility.HtmlEncode(path.Key)}">
                <button type="submit">Restore bundled version</button>
            </form>
            <nav class="editor-menu" aria-label="Admin navigation">
                <a href="{WebUtility.HtmlEncode(EditorUrl(path))}">Cancel and return to editor</a>
                <a href="/edit">Page listing</a>
            </nav>
            """);
        await context.Response.WriteAsync(PageLayout.Footer());
    }

    private static string EditorUrl(PagePath path) => "/edit"
        + string.Join('/', path.Key.Split('/').Select(Uri.EscapeDataString));
}
