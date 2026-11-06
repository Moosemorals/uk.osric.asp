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

/// <summary>Lists the pages bundled with the release and restores them over the live copies.</summary>
internal static class SeedsEndpoint {

    internal const string MessageKey = "admin.seeds.message";

    private const string RestoreAction = "/admin/seeds/restore";
    private const string RestoreAllAction = "/admin/seeds/restore-all";

    public static async Task ListAsync(HttpContext context, PageEditor pages, IAntiforgery antiforgery) {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        List<SeedRow> rows = [];
        foreach (PagePath path in PageAssetSeeder.SeedPaths()) {
            rows.Add(await RowAsync(pages, path, context.RequestAborted));
        }

        await ShellDocument.BeginAsync(context, "Seeded pages");
        await context.Response.WriteAsync("<h1>Seeded pages</h1>");
        await WriteMessageAsync(context);
        await context.Response.WriteAsync(
            "<p>Restoring replaces the live page with the version bundled with this release. "
            + "Revision history is retained; a page that already matches its seed offers no restore.</p>");
        await context.Response.WriteAsync(PageLayout.Nav("Admin navigation", [
            new("/edit", "Page listing"),
        ], "editor-menu"));
        if (rows.Exists(row => row.NeedsRestore)) {
            await context.Response.WriteAsync($"""
                <form method="post" action="{RestoreAllAction}">
                    {AntiforgeryField.Hidden(tokens)}
                    <button type="submit">Restore all seeds</button>
                </form>
                """);
        }

        await context.Response.WriteAsync("<table><thead><tr><th scope=\"col\">Page</th><th scope=\"col\">State</th><th scope=\"col\">Action</th></tr></thead><tbody>");
        foreach (SeedRow row in rows) {
            string key = WebUtility.HtmlEncode(row.Path.Key);
            string editor = WebUtility.HtmlEncode(PageUrls.Editor(row.Path));
            string action = string.Empty;
            if (row.NeedsRestore) {
                action = $"""
                        <form method="post" action="{RestoreAction}">
                            {AntiforgeryField.Hidden(tokens)}
                            <input type="hidden" name="path" value="{key}">
                            <button type="submit" aria-label="Restore {key} to its seed">Restore to seed</button>
                        </form>
                    """;
            }

            await context.Response.WriteAsync($"""
                <tr>
                    <td><a href="{editor}">{key}</a></td>
                    <td>{WebUtility.HtmlEncode(row.State)}</td>
                    <td>{action}</td>
                </tr>
                """);
        }
        await context.Response.WriteAsync("</tbody></table>");
        await ShellDocument.EndAsync(context);
    }

    public static async Task RestoreAsync(HttpContext context, PageEditor pages) {
        if (!CanReadPost(context)) {
            return;
        }

        IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted);
        if (!form.TryGetValue("path", out StringValues pathValues)
            || !TryGetPath(pathValues, out PagePath path)
            || !path.IsAddressable) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(path, context.RequestAborted);
        if (template is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        bool changed = await SaveSeedAsync(pages, path, template, context.RequestAborted);
        context.Session.SetString(MessageKey, changed
            ? $"Restored {path.Key}."
            : $"{path.Key} already matched its seed.");
        Redirect.SeeOther(context, "/admin");
    }

    public static async Task RestoreAllAsync(HttpContext context, PageEditor pages) {
        if (!CanReadPost(context)) {
            return;
        }

        int restored = 0;
        int matched = 0;
        foreach (PagePath path in PageAssetSeeder.SeedPaths()) {
            string? template = await PageAssetSeeder.ReadSeedTemplateAsync(path, context.RequestAborted);
            if (template is null) {
                continue;
            }

            if (await SaveSeedAsync(pages, path, template, context.RequestAborted)) {
                restored++;
            } else {
                matched++;
            }
        }

        context.Session.SetString(MessageKey, restored switch {
            0 => $"All {matched} seeded pages already matched.",
            1 => $"Restored 1 seeded page; {matched} already matched.",
            _ => $"Restored {restored} seeded pages; {matched} already matched.",
        });
        Redirect.SeeOther(context, "/admin");
    }

    private static async Task<SeedRow> RowAsync(PageEditor pages, PagePath path, CancellationToken cancellationToken) {
        PageHeadSummary? head = await pages.GetHeadAsync(path, cancellationToken);
        if (head is null) {
            return new SeedRow(path, "Missing", NeedsRestore: true);
        }

        if (head.IsDeleted) {
            return new SeedRow(path, "Deleted", NeedsRestore: true);
        }

        PageTemplate? live = await pages.GetAsync(path, cancellationToken);
        string? seed = await PageAssetSeeder.ReadSeedTemplateAsync(path, cancellationToken);
        if (live is null || seed is null) {
            return new SeedRow(path, "Unavailable", NeedsRestore: true);
        }

        bool matches = string.Equals(live.Template, seed, StringComparison.Ordinal);
        return new SeedRow(path, matches ? "Matches seed" : "Edited", NeedsRestore: !matches);
    }

    private static async Task<bool> SaveSeedAsync(
        PageEditor pages,
        PagePath path,
        string template,
        CancellationToken cancellationToken) {
        PageTemplate? live = await pages.GetAsync(path, cancellationToken);
        if (live is not null && string.Equals(live.Template, template, StringComparison.Ordinal)) {
            return false;
        }

        await pages.SaveAsync(path, template, cancellationToken);
        return true;
    }

    private static bool CanReadPost(HttpContext context) {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }
            || !context.Request.HasFormContentType) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return false;
        }

        return true;
    }

    private static bool TryGetPath(StringValues values, out PagePath path) {
        path = PagePath.Normalize(null);
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0])) {
            return false;
        }

        path = PagePath.Normalize(values[0]);
        return true;
    }

    private static async Task WriteMessageAsync(HttpContext context) {
        string? message = context.Session.GetString(MessageKey);
        if (message is null) {
            return;
        }

        context.Session.Remove(MessageKey);
        if (!string.IsNullOrEmpty(message)) {
            await context.Response.WriteAsync($"<p>{WebUtility.HtmlEncode(message)}</p>");
        }
    }

    private readonly record struct SeedRow(PagePath Path, string State, bool NeedsRestore);
}
