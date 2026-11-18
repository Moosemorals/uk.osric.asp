// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using System.Net;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Shell;

/// <summary>The public index of the tools the shell offers.</summary>
/// <remarks>
/// It answers anyone: an anonymous visitor gets the public tools, and the owner's are left out rather
/// than answered with a redirect. The list is the shell's own — the same values the administration
/// views' menu draws — so the index cannot offer a tool the menu has forgotten or miss one it has.
/// </remarks>
internal static class AdminIndexEndpoint {

    /// <summary>Answers <c>GET /admin</c>.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    public static async Task HandleAsync(HttpContext context, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);

        bool authenticated = context.User.Identity?.IsAuthenticated == true;
        string editorPath = EditorPaths.Of(context.RequestServices);
        IReadOnlyList<ShellLink> links = [
            .. ShellCatalogue.Links(
                    context.Request.Path,
                    context.Request.QueryString.Value ?? string.Empty,
                    editorPath)
                .Where(link => link.Section == ShellSection.Administration
                    && ShellCatalogue.IsFor(link, authenticated)
                    // The index is this page; a list of one is not an index.
                    && !string.Equals(link.Href, "/admin", StringComparison.Ordinal)),
        ];

        await ShellDocument.BeginAsync(
            context,
            "Administration",
            SiteNavigation.For(context),
            stylesheets: ["/admin.css"],
            cancellationToken: cancellationToken);

        await context.Response.WriteAsync("<h1>Administration</h1>", cancellationToken);
        await context.Response.WriteAsync(
            "<p>What this site lets its owner change. A page is edited where it is served: "
            + "the tools here are the ones a page cannot do for itself.</p>",
            cancellationToken);

        await context.Response.WriteAsync("<ul class=\"admin-pages\" role=\"list\">", cancellationToken);
        foreach (ShellLink link in links) {
            await context.Response.WriteAsync(
                $"<li><a href=\"{WebUtility.HtmlEncode(link.Href)}\">{WebUtility.HtmlEncode(link.Text)}</a></li>",
                cancellationToken);
        }

        await context.Response.WriteAsync("</ul>", cancellationToken);

        if (!authenticated) {
            await context.Response.WriteAsync(
                "<p>The rest of the administration is for the owner. Sign in and this list gains the "
                + "tools that change what the site serves.</p>",
                cancellationToken);
        } else {
            await context.Response.WriteAsync(
                "<p>The <a href=\"/help\">author manual</a> explains each tool, and "
                + "<a href=\"/admin/seed-apply\">Apply seed updates</a> is the routine way a release "
                + "moves a page.</p>",
                cancellationToken);
        }

        await ShellDocument.EndAsync(context, cancellationToken);
    }
}
