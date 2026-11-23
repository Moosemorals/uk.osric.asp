// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using System.Globalization;
using System.Net;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Pages;

/// <summary>The changelist: every commit in the deployed build, newest first.</summary>
/// <remarks>
/// The list is a file written by <c>make changelist</c> and packed beside the assembly, because the
/// running image has no source tree and no git. Each line is a full hash and a subject, which is what
/// lets the hash link to the commit's own page on GitHub instead of being decoration.
/// </remarks>
internal static class ChangesEndpoint {

    /// <summary>Renders the changelist.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="changeList">The changelist and why it is what it is.</param>
    public static async Task HandleAsync(HttpContext context, ChangeList changeList) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(changeList);

        ChangeListRead read = changeList.Read;
        IReadOnlyList<SiteCommit> commits = read.Commits;

        await ShellDocument.BeginAsync(context, "Changelist", SiteNavigation.For(context), stylesheets: ["/admin.css"]);
        await context.Response.WriteAsync("<h1>Changelist</h1>");
        await context.Response.WriteAsync(AdminNavigation.Nav(context));

        if (read.Reason is ChangeListReason.Missing) {
            await context.Response.WriteAsync("""
                <p>This build carries no changelist. <code>make changelist</code> writes it and the
                build packs it, so a build that did not come through the Makefile has none.</p>
                """);
        } else if (read.Reason is ChangeListReason.Unreadable) {
            await context.Response.WriteAsync("""
                <p>This build's changelist could not be read. The journal has the reason; the page
                will answer again once the file can be read.</p>
                """);
        } else {
            await context.Response.WriteAsync($"<p>{commits.Count.ToString(CultureInfo.InvariantCulture)} commits, newest first. Each links to its page on GitHub.</p>");
            await context.Response.WriteAsync("<ul class=\"changelist\">");

            foreach (SiteCommit commit in commits) {
                string subject = WebUtility.HtmlEncode(commit.Subject);
                await context.Response.WriteAsync(commit.Hash is { } hash
                    ? $"""<li><a href="{SiteIdentity.RepositoryUrl}/commit/{hash}"><code>{Short(hash)}</code> {subject}</a></li>"""
                    : $"<li>{subject}</li>");
            }

            await context.Response.WriteAsync("</ul>");
        }

        await ShellDocument.EndAsync(context);
    }

    private static string Short(string hash) => hash[..7];
}
