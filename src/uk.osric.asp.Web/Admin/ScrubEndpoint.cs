// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Primitives;
using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

/// <summary>Scrubs a deleted page and the history it retained, once its key is typed.</summary>
/// <remarks>
/// <para>
/// Scrubbing is the one page operation that cannot be undone, so it stays in the shell rather than
/// becoming a page capability: the confirmation is a view a stored page cannot shadow, and the
/// typed key is checked here rather than by whatever page is currently the editor.
/// </para>
/// <para>
/// Only a deleted page can be scrubbed. A live page answers <c>409</c> and names the delete that has
/// to come first, so the destructive request is never the first one to touch a page that is still
/// being served.
/// </para>
/// </remarks>
internal static class ScrubEndpoint {

    /// <summary>Shows the confirmation for the page named by the route.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="pages">The page store.</param>
    /// <param name="antiforgery">Mints the form's token.</param>
    public static async Task ConfirmAsync(HttpContext context, PageEditor pages, IAntiforgery antiforgery) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pages);

        PagePath path = PathOf(context);
        PageHeadSummary? head = await pages.GetHeadAsync(path, context.RequestAborted);
        if (head is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        if (!head.IsDeleted) {
            await WriteRequiresDeletedPageAsync(context, path);
            return;
        }

        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        await ShellDocument.BeginAsync(context, "Scrub page history", SiteNavigation.For(context));
        await context.Response.WriteAsync($"<h1>Scrub {WebUtility.HtmlEncode(path.Key)}?</h1>");
        await context.Response.WriteAsync("""
            <p>This irreversibly removes this deleted page and all retained revisions. It cannot be undone and is intended for cases such as committed secrets.</p>
            <p>Scrubbing does not revoke already-running page code or remove browser/CDN copies, exported archives, database backups, or filesystem snapshots. Rotate exposed secrets and clean those copies separately. It cannot promise forensic erasure from SSDs or external copies.</p>
            """);
        await context.Response.WriteAsync($"""
            <form method="post" action="{WebUtility.HtmlEncode(PageUrls.Scrub(path))}" class="form">
                {AntiforgeryField.Hidden(tokens)}
                <dl>
                    <dt><label for="confirmation">Type {WebUtility.HtmlEncode(path.Key)} to confirm</label></dt>
                    <dd><input id="confirmation" name="confirmation" required></dd>
                </dl>
                <button type="submit">Permanently scrub</button>
            </form>
            {ShellDocument.Nav("Confirmation navigation", [new(PageUrls.Editor(path, EditorPaths.Of(context.RequestServices)), "Cancel")], "editor-menu")}
            """);
        await ShellDocument.EndAsync(context);
    }

    /// <summary>Scrubs the page named by the route, if the typed key matches.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="pages">The page store.</param>
    /// <param name="loggers">Names the log category.</param>
    public static async Task ScrubAsync(HttpContext context, PageEditor pages, ILoggerFactory loggers) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(loggers);

        PagePath path = PathOf(context);
        if (!context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("confirmation", out StringValues confirmation)
            || confirmation.Count != 1
            || !string.Equals(confirmation[0], path.Key, StringComparison.Ordinal)) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "editor.scrub_confirmation",
                "Scrub was not confirmed",
                $"The confirmation did not match {path.Key}, so nothing was removed. Type the page's key exactly to confirm.",
                [new(PageUrls.Scrub(path), "Back to confirmation")]));
            return;
        }

        PageHeadSummary? head = await pages.GetHeadAsync(path, context.RequestAborted);
        if (head is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        if (!head.IsDeleted) {
            await WriteRequiresDeletedPageAsync(context, path);
            return;
        }

        if (!await pages.ScrubAsync(path, context.RequestAborted)) {
            // The head can move between the read above and the scrub, so the answer is derived from
            // what the store says now rather than from the reason the scrub refused.
            PageHeadSummary? current = await pages.GetHeadAsync(path, context.RequestAborted);
            if (current is null) {
                await NotFound.WriteAsync(context, path);
            } else {
                await WriteRequiresDeletedPageAsync(context, path);
            }

            return;
        }

        loggers.CreateLogger(typeof(ScrubEndpoint).FullName!)
            .LogInformation("Scrubbed {Path} and its retained history", path.Key);

        Redirect.SeeOther(context, EditorPaths.Of(context.RequestServices));
    }

    private static PagePath PathOf(HttpContext context) =>
        PagePath.Normalize(context.Request.RouteValues["path"] as string);

    private static Task WriteRequiresDeletedPageAsync(HttpContext context, PagePath path) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status409Conflict, new ErrorMessage(
            "editor.scrub_live_page",
            "Delete the page before scrubbing its history",
            $"Scrubbing removes the retained history of a deleted page, so {path.Key} has to be deleted first. Nothing was removed.",
            [new(PageUrls.EditorCommand(path, "delete", EditorPaths.Of(context.RequestServices)), "Delete the page"), new(EditorPaths.Of(context.RequestServices), "Page list")]));
}
