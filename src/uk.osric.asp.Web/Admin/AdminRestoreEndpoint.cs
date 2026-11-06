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

/// <summary>Restores a shipped page over the live one, behind a confirmation form.</summary>
internal static class AdminRestoreEndpoint {

    public static async Task GetAsync(HttpContext context, IAntiforgery antiforgery) {
        if (!TryGetPath(context.Request.Query["path"], out PagePath path)) {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!path.IsAddressable) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(path, context.RequestAborted);
        if (template is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        await WriteConfirmationAsync(context, path, template, tokens);
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

        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(path, context.RequestAborted);
        if (template is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        await pages.SaveAsync(path, template, context.RequestAborted);
        context.Session.SetString(EditorEndpoint.MessageKey, "Restored the bundled version.");
        Redirect.SeeOther(context, PageUrls.Editor(path));
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
        string template,
        AntiforgeryTokenSet tokens) {
        await ShellDocument.BeginAsync(context, "Restore bundled version", stylesheet: "/editor.css");
        await context.Response.WriteAsync($"<h1>Restore {WebUtility.HtmlEncode(path.Key)}</h1>");
        await context.Response.WriteAsync(
            "This will replace the current page template with the version bundled with this release. "
            + "The existing revision history will be retained.");
        await context.Response.WriteAsync($"<pre class=\"source\" tabindex=\"0\">{WebUtility.HtmlEncode(template)}</pre>");
        await context.Response.WriteAsync($"""
            <form method="post" action="/admin/restore">
                {AntiforgeryField.Hidden(tokens)}
                <input type="hidden" name="path" value="{WebUtility.HtmlEncode(path.Key)}">
                <button type="submit">Restore bundled version</button>
            </form>
            """);
        await context.Response.WriteAsync(PageLayout.Nav("Admin navigation", [
            new(PageUrls.Editor(path), "Cancel and return to editor"),
            new("/edit", "Page listing"),
        ], "editor-menu"));
        await ShellDocument.EndAsync(context);
    }
}
