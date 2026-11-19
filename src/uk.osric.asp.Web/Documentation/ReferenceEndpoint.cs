// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Documentation;

using System.Globalization;
using System.Net;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Pages;

/// <summary>The two pages that read the documentation this build carries.</summary>
/// <remarks>
/// The shell owns these routes because the XML is a build artifact the image carries, like the
/// changelist: a page cannot read a file beside the assembly. Both are public even though the key is
/// under <c>/admin</c> — the URL is a filing cabinet, not an authorisation — and both render the
/// admin menu because that is where a reader finds them.
/// </remarks>
internal static class ReferenceEndpoint {

    /// <summary>Answers <c>GET /admin/reference</c>: this build's assemblies.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="library">The corpus this build carries.</param>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    public static async Task IndexAsync(
        HttpContext context,
        ReferenceLibrary library,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(library);

        await ShellDocument.BeginAsync(
            context,
            "Reference",
            SiteNavigation.For(context),
            stylesheets: ["/reference.css"],
            cancellationToken: cancellationToken);

        await context.Response.WriteAsync("<h1>Reference</h1>", cancellationToken);
        await context.Response.WriteAsync(AdminMenu(context), cancellationToken);
        await context.Response.WriteAsync(
            "<p>The documentation this build's assemblies carry, generated from their source comments. "
            + "It describes the code serving this request, not a version on GitHub.</p>",
            cancellationToken);

        if (BuildInfo.Current.Commit is { } commit) {
            string shortCommit = commit[..Math.Min(7, commit.Length)];
            await context.Response.WriteAsync(
                $"<p class=\"reference-build\">Built from <a href=\"{ChangesEndpoint.RepositoryUrl}/commit/{WebUtility.HtmlEncode(commit)}\"><code>{WebUtility.HtmlEncode(shortCommit)}</code></a>.</p>",
                cancellationToken);
        }

        await context.Response.WriteAsync("<h2>Assemblies</h2>", cancellationToken);

        if (library.Assemblies.Count == 0) {
            await context.Response.WriteAsync(
                "<p>This build carries no documentation. <code>GenerateDocumentationFile</code> writes it and "
                + "the publish copies it beside each assembly, so a build that did not come through the "
                + "Makefile has none.</p>",
                cancellationToken);
        } else {
            await context.Response.WriteAsync("<ul class=\"reference-assemblies\">", cancellationToken);
            foreach (ReferenceAssembly assembly in library.Assemblies) {
                await context.Response.WriteAsync(
                    $"<li><a href=\"/admin/reference/{WebUtility.HtmlEncode(assembly.Info.Slug)}\">{WebUtility.HtmlEncode(assembly.Info.Title)}</a> "
                    + $"<span class=\"reference-count\">{Count(assembly.TypeCount, "type", "types")}, {Count(assembly.EntryCount, "entry", "entries")}</span><br>"
                    + $"<code>{WebUtility.HtmlEncode(assembly.Info.AssemblyName)}</code> — {WebUtility.HtmlEncode(assembly.Info.Description)}</li>",
                    cancellationToken);
            }

            await context.Response.WriteAsync("</ul>", cancellationToken);
        }

        await context.Response.WriteAsync("""
            <h2>What this page shows</h2>
            <p>Every entry is the documentation the compiler emitted for this build, so it is not a filtered
            public surface: a documented <code>internal</code> type is here too.</p>
            <p>A member that inherits its documentation carries the source's words and the line that says
            where they came from. Where the source is outside this build — the BCL, EF Core — the entry names
            it and links to its page on Microsoft Learn when the URL can be computed.</p>
            """, cancellationToken);

        await ShellDocument.EndAsync(context, cancellationToken);
    }

    /// <summary>Answers <c>GET /admin/reference/{slug}</c>: one assembly.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="slug">The assembly's URL segment.</param>
    /// <param name="library">The corpus this build carries.</param>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    public static async Task AssemblyAsync(
        HttpContext context,
        string slug,
        ReferenceLibrary library,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(library);

        if (library.Find(slug) is not { } assembly) {
            bool isAuthenticated = context.User.Identity?.IsAuthenticated == true;
            await ErrorResponses.WriteAsync(
                context,
                StatusCodes.Status404NotFound,
                new ErrorMessage(
                    "reference.not_found",
                    "Not found",
                    $"There is no documentation for \u201c{slug}\u201d in this build. The reference lists the assemblies it carries.",
                    [
                        new NavLink("/admin/reference", "All assemblies"),
                        .. ErrorMessage.DefaultRemedies(isAuthenticated, EditorPaths.Of(context.RequestServices)),
                    ]),
                cancellationToken);
            return;
        }

        await ShellDocument.BeginAsync(
            context,
            assembly.Info.Title,
            SiteNavigation.For(context),
            stylesheets: ["/reference.css"],
            cancellationToken: cancellationToken);

        await context.Response.WriteAsync($"<h1>{WebUtility.HtmlEncode(assembly.Info.Title)}</h1>", cancellationToken);
        await context.Response.WriteAsync(AdminMenu(context), cancellationToken);
        await context.Response.WriteAsync(
            $"<p class=\"reference-meta\"><a href=\"/admin/reference\">All assemblies</a> · "
            + $"<code>{WebUtility.HtmlEncode(assembly.Info.AssemblyName)}</code> · "
            + $"{Count(assembly.TypeCount, "type", "types")} · {Count(assembly.EntryCount, "entry", "entries")}</p>",
            cancellationToken);

        if (library.Render(slug) is { } fragment) {
            await context.Response.WriteAsync(fragment, cancellationToken);
        }

        await ShellDocument.EndAsync(context, cancellationToken);
    }

    private static string AdminMenu(HttpContext context) =>
        AdminNavigation.Nav(context);

    // "entry" does not pluralise with an "s", so the caller names both forms.
    private static string Count(int count, string singular, string plural) =>
        count.ToString("N0", CultureInfo.InvariantCulture) + " " + (count == 1 ? singular : plural);
}
